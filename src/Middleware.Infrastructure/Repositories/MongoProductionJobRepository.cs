using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Middleware.Infrastructure.Repositories;

public sealed class MongoProductionJobRepository : IProductionJobRepository
{
    private readonly IMongoCollection<ProductionJobDocument> collection;

    public MongoProductionJobRepository(IOptions<MiddlewareOptions> options)
    {
        var mongoOptions = options.Value.Mongo;
        var client = new MongoClient(mongoOptions.ConnectionString);
        var database = client.GetDatabase(mongoOptions.Database);
        collection = database.GetCollection<ProductionJobDocument>(mongoOptions.ProductionJobCollection);
    }

    public async Task InsertAsync(ProductionJob job, CancellationToken cancellationToken)
    {
        await collection.InsertOneAsync(ProductionJobDocument.FromDomain(job), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyCollection<ProductionJob>> ListAsync(
        ProductionJobQuery query,
        CancellationToken cancellationToken)
    {
        var documents = await collection
            .Find(BuildFilter(query))
            .SortByDescending(document => document.RequestedAtUtc)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToList();
    }

    public async Task<ProductionJob?> GetByJobIdAsync(string jobId, CancellationToken cancellationToken)
    {
        var document = await collection
            .Find(item => item.JobId == jobId)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDomain();
    }

    public async Task<ProductionJob?> ClaimNextQueuedAsync(CancellationToken cancellationToken)
    {
        var filter = Builders<ProductionJobDocument>.Filter.Eq(item => item.Status, ProductionJobStatuses.Queued);
        var update = Builders<ProductionJobDocument>.Update.Set(item => item.Status, ProductionJobStatuses.Validating);
        var options = new FindOneAndUpdateOptions<ProductionJobDocument>
        {
            Sort = Builders<ProductionJobDocument>.Sort.Ascending(item => item.RequestedAtUtc),
            ReturnDocument = ReturnDocument.After
        };

        var document = await collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        return document?.ToDomain();
    }

    public async Task UpdateAsync(ProductionJob job, CancellationToken cancellationToken)
    {
        await collection.ReplaceOneAsync(
            item => item.JobId == job.JobId,
            ProductionJobDocument.FromDomain(job),
            new ReplaceOptions { IsUpsert = false },
            cancellationToken);
    }

    private static FilterDefinition<ProductionJobDocument> BuildFilter(ProductionJobQuery query)
    {
        var builder = Builders<ProductionJobDocument>.Filter;
        var filters = new List<FilterDefinition<ProductionJobDocument>>();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            filters.Add(builder.Eq(item => item.Status, query.Status.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.DesignId))
        {
            filters.Add(builder.Eq(item => item.DesignId, query.DesignId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.UseCase))
        {
            filters.Add(builder.Eq(item => item.UseCase, query.UseCase.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.RequestedBy))
        {
            filters.Add(builder.Eq(item => item.RequestedBy, query.RequestedBy.Trim()));
        }

        return filters.Count == 0 ? builder.Empty : builder.And(filters);
    }

    private sealed class ProductionJobDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("job_id")]
        public string JobId { get; set; } = string.Empty;

        [BsonElement("design_id")]
        public string DesignId { get; set; } = string.Empty;

        [BsonElement("design_name")]
        public string DesignName { get; set; } = string.Empty;

        [BsonElement("use_case")]
        public string UseCase { get; set; } = string.Empty;

        [BsonElement("requested_by")]
        public string RequestedBy { get; set; } = string.Empty;

        [BsonElement("requested_at_utc")]
        public DateTime RequestedAtUtc { get; set; }

        [BsonElement("status")]
        public string Status { get; set; } = string.Empty;

        [BsonElement("current_step_sequence")]
        public int? CurrentStepSequence { get; set; }

        [BsonElement("current_step_name")]
        public string? CurrentStepName { get; set; }

        [BsonElement("started_at_utc")]
        public DateTime? StartedAtUtc { get; set; }

        [BsonElement("completed_at_utc")]
        public DateTime? CompletedAtUtc { get; set; }

        [BsonElement("failure_reason")]
        public string? FailureReason { get; set; }

        [BsonElement("events")]
        public List<ProductionJobEventDocument> Events { get; set; } = [];

        public static ProductionJobDocument FromDomain(ProductionJob job)
        {
            return new ProductionJobDocument
            {
                JobId = job.JobId,
                DesignId = job.DesignId,
                DesignName = job.DesignName,
                UseCase = job.UseCase,
                RequestedBy = job.RequestedBy,
                RequestedAtUtc = job.RequestedAtUtc.UtcDateTime,
                Status = job.Status,
                CurrentStepSequence = job.CurrentStepSequence,
                CurrentStepName = job.CurrentStepName,
                StartedAtUtc = job.StartedAtUtc?.UtcDateTime,
                CompletedAtUtc = job.CompletedAtUtc?.UtcDateTime,
                FailureReason = job.FailureReason,
                Events = job.Events.Select(ProductionJobEventDocument.FromDomain).ToList()
            };
        }

        public ProductionJob ToDomain()
        {
            return new ProductionJob(
                JobId,
                DesignId,
                DesignName,
                UseCase,
                RequestedBy,
                new DateTimeOffset(RequestedAtUtc, TimeSpan.Zero),
                Status,
                CurrentStepSequence,
                CurrentStepName,
                StartedAtUtc is null ? null : new DateTimeOffset(StartedAtUtc.Value, TimeSpan.Zero),
                CompletedAtUtc is null ? null : new DateTimeOffset(CompletedAtUtc.Value, TimeSpan.Zero),
                FailureReason,
                Events.Select(item => item.ToDomain()).ToList());
        }
    }

    private sealed class ProductionJobEventDocument
    {
        [BsonElement("occurred_at_utc")]
        public DateTime OccurredAtUtc { get; set; }

        [BsonElement("level")]
        public string Level { get; set; } = string.Empty;

        [BsonElement("message")]
        public string Message { get; set; } = string.Empty;

        [BsonElement("step_sequence")]
        public int? StepSequence { get; set; }

        [BsonElement("step_name")]
        public string? StepName { get; set; }

        public static ProductionJobEventDocument FromDomain(ProductionJobEvent jobEvent)
        {
            return new ProductionJobEventDocument
            {
                OccurredAtUtc = jobEvent.OccurredAtUtc.UtcDateTime,
                Level = jobEvent.Level,
                Message = jobEvent.Message,
                StepSequence = jobEvent.StepSequence,
                StepName = jobEvent.StepName
            };
        }

        public ProductionJobEvent ToDomain()
        {
            return new ProductionJobEvent(
                new DateTimeOffset(OccurredAtUtc, TimeSpan.Zero),
                Level,
                Message,
                StepSequence,
                StepName);
        }
    }
}

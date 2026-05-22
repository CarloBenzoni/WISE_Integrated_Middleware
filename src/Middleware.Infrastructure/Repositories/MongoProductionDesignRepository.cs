using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Middleware.Infrastructure.Repositories;

public sealed class MongoProductionDesignRepository : IProductionDesignRepository
{
    private readonly IMongoCollection<ProductionDesignDocument> collection;

    public MongoProductionDesignRepository(IOptions<MiddlewareOptions> options)
    {
        var mongoOptions = options.Value.Mongo;
        var client = new MongoClient(mongoOptions.ConnectionString);
        var database = client.GetDatabase(mongoOptions.Database);
        collection = database.GetCollection<ProductionDesignDocument>(mongoOptions.ProductionDesignCollection);
    }

    public async Task InsertAsync(ProductionDesign design, CancellationToken cancellationToken)
    {
        await collection.InsertOneAsync(ProductionDesignDocument.FromDomain(design), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyCollection<ProductionDesign>> ListAsync(
        ProductionDesignQuery query,
        CancellationToken cancellationToken)
    {
        var documents = await collection
            .Find(BuildFilter(query))
            .SortByDescending(document => document.DesignedAtUtc)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToList();
    }

    public async Task<ProductionDesign?> GetByDesignIdAsync(string designId, CancellationToken cancellationToken)
    {
        var document = await collection
            .Find(item => item.DesignId == designId)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDomain();
    }

    private static FilterDefinition<ProductionDesignDocument> BuildFilter(ProductionDesignQuery query)
    {
        var builder = Builders<ProductionDesignDocument>.Filter;
        var filters = new List<FilterDefinition<ProductionDesignDocument>>();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var escaped = System.Text.RegularExpressions.Regex.Escape(query.Search.Trim());
            var regex = new BsonRegularExpression(escaped, "i");
            filters.Add(builder.Or(
                builder.Regex(item => item.DesignName, regex),
                builder.Regex(item => item.ProductIdentifier, regex),
                builder.Regex(item => item.UseCase, regex)));
        }

        if (!string.IsNullOrWhiteSpace(query.UseCase))
        {
            filters.Add(builder.Eq(item => item.UseCase, query.UseCase.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.DesignedBy))
        {
            filters.Add(builder.Eq(item => item.DesignedBy, query.DesignedBy.Trim()));
        }

        if (query.FromUtc.HasValue)
        {
            filters.Add(builder.Gte(item => item.DesignedAtUtc, query.FromUtc.Value.UtcDateTime));
        }

        if (query.ToUtc.HasValue)
        {
            filters.Add(builder.Lte(item => item.DesignedAtUtc, query.ToUtc.Value.UtcDateTime));
        }

        return filters.Count == 0 ? builder.Empty : builder.And(filters);
    }

    private sealed class ProductionDesignDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("design_id")]
        public string DesignId { get; set; } = string.Empty;

        [BsonElement("design_name")]
        public string DesignName { get; set; } = string.Empty;

        [BsonElement("use_case")]
        public string UseCase { get; set; } = string.Empty;

        [BsonElement("product_identifier")]
        public string ProductIdentifier { get; set; } = string.Empty;

        [BsonElement("designed_by")]
        public string DesignedBy { get; set; } = string.Empty;

        [BsonElement("designed_at_utc")]
        public DateTime DesignedAtUtc { get; set; }

        [BsonElement("notes")]
        public string? Notes { get; set; }

        [BsonElement("related_documents")]
        public List<ProductionDocumentLinkDocument> RelatedDocuments { get; set; } = [];

        [BsonElement("process_steps")]
        public List<ProductionProcessStepDefinitionDocument> ProcessSteps { get; set; } = [];

        public static ProductionDesignDocument FromDomain(ProductionDesign design)
        {
            return new ProductionDesignDocument
            {
                DesignId = design.DesignId,
                DesignName = design.DesignName,
                UseCase = design.UseCase,
                ProductIdentifier = design.ProductIdentifier,
                DesignedBy = design.DesignedBy,
                DesignedAtUtc = design.DesignedAtUtc.UtcDateTime,
                Notes = design.Notes,
                RelatedDocuments = design.RelatedDocuments.Select(ProductionDocumentLinkDocument.FromDomain).ToList(),
                ProcessSteps = design.ProcessSteps.Select(ProductionProcessStepDefinitionDocument.FromDomain).ToList()
            };
        }

        public ProductionDesign ToDomain()
        {
            return new ProductionDesign(
                DesignId,
                DesignName,
                UseCase,
                ProductIdentifier,
                DesignedBy,
                new DateTimeOffset(DesignedAtUtc, TimeSpan.Zero),
                Notes,
                RelatedDocuments.Select(document => document.ToDomain()).ToList(),
                ProcessSteps.Select(step => step.ToDomain()).OrderBy(step => step.Sequence).ToList());
        }
    }

    private sealed class ProductionDocumentLinkDocument
    {
        [BsonElement("document_id")]
        public string DocumentId { get; set; } = string.Empty;

        [BsonElement("role")]
        public string Role { get; set; } = string.Empty;

        [BsonElement("category")]
        public string? Category { get; set; }

        [BsonElement("file_name")]
        public string? FileName { get; set; }

        public static ProductionDocumentLinkDocument FromDomain(ProductionDocumentLink document)
        {
            return new ProductionDocumentLinkDocument
            {
                DocumentId = document.DocumentId,
                Role = document.Role,
                Category = document.Category,
                FileName = document.FileName
            };
        }

        public ProductionDocumentLink ToDomain()
        {
            return new ProductionDocumentLink(DocumentId, Role, Category, FileName);
        }
    }

    private sealed class ProductionProcessStepDefinitionDocument
    {
        [BsonElement("sequence")]
        public int Sequence { get; set; }

        [BsonElement("process_name")]
        public string ProcessName { get; set; } = string.Empty;

        [BsonElement("process_label")]
        public string ProcessLabel { get; set; } = string.Empty;

        [BsonElement("requires_machine_execution")]
        public bool RequiresMachineExecution { get; set; }

        [BsonElement("selected_recipe_name")]
        public string? SelectedRecipeName { get; set; }

        [BsonElement("selected_recipe_index")]
        public int? SelectedRecipeIndex { get; set; }

        [BsonElement("part_program_document_id")]
        public string? PartProgramDocumentId { get; set; }

        [BsonElement("part_program_file_name")]
        public string? PartProgramFileName { get; set; }

        [BsonElement("supporting_document_ids")]
        public List<string> SupportingDocumentIds { get; set; } = [];

        [BsonElement("parameters")]
        public List<ProductionParameterSnapshotDocument> Parameters { get; set; } = [];

        public static ProductionProcessStepDefinitionDocument FromDomain(ProductionProcessStepDefinition step)
        {
            return new ProductionProcessStepDefinitionDocument
            {
                Sequence = step.Sequence,
                ProcessName = step.ProcessName,
                ProcessLabel = step.ProcessLabel,
                RequiresMachineExecution = step.RequiresMachineExecution,
                SelectedRecipeName = step.SelectedRecipeName,
                SelectedRecipeIndex = step.SelectedRecipeIndex,
                PartProgramDocumentId = step.PartProgramDocumentId,
                PartProgramFileName = step.PartProgramFileName,
                SupportingDocumentIds = step.SupportingDocumentIds.ToList(),
                Parameters = step.Parameters.Select(ProductionParameterSnapshotDocument.FromDomain).ToList()
            };
        }

        public ProductionProcessStepDefinition ToDomain()
        {
            return new ProductionProcessStepDefinition(
                Sequence,
                ProcessName,
                ProcessLabel,
                RequiresMachineExecution,
                SelectedRecipeName,
                SelectedRecipeIndex,
                PartProgramDocumentId,
                PartProgramFileName,
                SupportingDocumentIds,
                Parameters.Select(parameter => parameter.ToDomain()).ToList());
        }
    }

    private sealed class ProductionParameterSnapshotDocument
    {
        [BsonElement("key")]
        public string Key { get; set; } = string.Empty;

        [BsonElement("label")]
        public string Label { get; set; } = string.Empty;

        [BsonElement("unit")]
        public string? Unit { get; set; }

        [BsonElement("value")]
        public string Value { get; set; } = string.Empty;

        public static ProductionParameterSnapshotDocument FromDomain(ProductionParameterSnapshot parameter)
        {
            return new ProductionParameterSnapshotDocument
            {
                Key = parameter.Key,
                Label = parameter.Label,
                Unit = parameter.Unit,
                Value = parameter.Value
            };
        }

        public ProductionParameterSnapshot ToDomain()
        {
            return new ProductionParameterSnapshot(Key, Label, Unit, Value);
        }
    }
}

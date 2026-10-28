using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Driver;

namespace Middleware.Infrastructure.Repositories;

public sealed class MongoTwinCatTelemetryRepository : ITwinCatTelemetryRepository
{
    private readonly IMongoCollection<TwinCatTelemetryDocument> collection;

    public MongoTwinCatTelemetryRepository(IOptions<MiddlewareOptions> options)
    {
        var mongoOptions = options.Value.Mongo;
        var client = new MongoClient(mongoOptions.ConnectionString);
        var database = client.GetDatabase(mongoOptions.Database);
        collection = database.GetCollection<TwinCatTelemetryDocument>(mongoOptions.TwinCatTelemetryCollection);
    }

    public async Task InsertAsync(TwinCatTelemetrySample sample, CancellationToken cancellationToken)
    {
        await collection.InsertOneAsync(
            TwinCatTelemetryDocument.FromDomain(sample),
            cancellationToken: cancellationToken);
    }

    private sealed class TwinCatTelemetryDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("record_type")]
        public string RecordType { get; set; } = "twincat_telemetry_sample";

        [BsonElement("source_type")]
        public string SourceType { get; set; } = "twincat";

        [BsonElement("source_id")]
        public string SourceId { get; set; } = string.Empty;

        [BsonElement("group_id")]
        public string GroupId { get; set; } = string.Empty;

        [BsonElement("observed_at_utc")]
        public DateTime ObservedAtUtc { get; set; }

        [BsonElement("ingested_at_utc")]
        public DateTime IngestedAtUtc { get; set; }

        [BsonElement("payload")]
        [BsonDictionaryOptions(DictionaryRepresentation.Document)]
        public Dictionary<string, TwinCatValueDocument> Payload { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [BsonElement("read_errors")]
        [BsonDictionaryOptions(DictionaryRepresentation.Document)]
        public Dictionary<string, string> ReadErrors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static TwinCatTelemetryDocument FromDomain(TwinCatTelemetrySample sample) => new()
        {
            SourceId = sample.SourceId,
            GroupId = sample.GroupId,
            ObservedAtUtc = sample.ObservedAtUtc.UtcDateTime,
            IngestedAtUtc = sample.IngestedAtUtc.UtcDateTime,
            Payload = sample.Payload.ToDictionary(
                item => item.Key,
                item => TwinCatValueDocument.FromDomain(item.Value),
                StringComparer.OrdinalIgnoreCase),
            ReadErrors = sample.ReadErrors.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private sealed class TwinCatValueDocument
    {
        [BsonElement("raw_value")]
        public string RawValue { get; set; } = string.Empty;

        [BsonElement("numeric_value")]
        [BsonIgnoreIfNull]
        public double? NumericValue { get; set; }

        [BsonElement("value_type")]
        public string ValueType { get; set; } = string.Empty;

        public static TwinCatValueDocument FromDomain(PlcTelemetryValue value) => new()
        {
            RawValue = value.RawValue,
            NumericValue = value.NumericValue,
            ValueType = value.ValueType
        };
    }
}

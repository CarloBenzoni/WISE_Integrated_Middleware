using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Driver;

namespace Middleware.Infrastructure.Repositories;

public sealed class MongoPlcTelemetrySampleRepository : IPlcTelemetrySampleRepository
{
    private readonly IMongoCollection<PlcTelemetryEnvelopeDocument> collection;

    public MongoPlcTelemetrySampleRepository(IOptions<MiddlewareOptions> options)
    {
        var mongoOptions = options.Value.Mongo;
        var client = new MongoClient(mongoOptions.ConnectionString);
        var database = client.GetDatabase(mongoOptions.Database);
        collection = database.GetCollection<PlcTelemetryEnvelopeDocument>(mongoOptions.PlcTelemetryCollection);
    }

    public async Task InsertAsync(PlcTelemetrySample sample, CancellationToken cancellationToken)
    {
        await collection.InsertOneAsync(
            PlcTelemetryEnvelopeDocument.FromDomain(sample.ToEnvelope()),
            cancellationToken: cancellationToken);
    }

    public async Task<PlcTelemetrySample?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var document = await collection
            .Find(FilterDefinition<PlcTelemetryEnvelopeDocument>.Empty)
            .SortByDescending(item => item.IngestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDomain();
    }

    public async Task<IReadOnlyCollection<PlcTelemetrySample>> QueryRangeAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PlcTelemetryEnvelopeDocument>.Filter.Gte(item => item.IngestedAtUtc, startUtc.UtcDateTime) &
                     Builders<PlcTelemetryEnvelopeDocument>.Filter.Lte(item => item.IngestedAtUtc, endUtc.UtcDateTime);

        var documents = await collection
            .Find(filter)
            .SortBy(item => item.IngestedAtUtc)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToList();
    }

    private sealed class PlcTelemetryEnvelopeDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("record_type")]
        public string RecordType { get; set; } = string.Empty;

        [BsonElement("source_type")]
        public string SourceType { get; set; } = string.Empty;

        [BsonElement("source_id")]
        public string SourceId { get; set; } = string.Empty;

        [BsonElement("timestamp_utc")]
        public DateTime TimestampUtc { get; set; }

        [BsonElement("ingested_at_utc")]
        public DateTime IngestedAtUtc { get; set; }

        [BsonElement("payload")]
        public PlcTelemetryPayloadDocument Payload { get; set; } = new();

        public static PlcTelemetryEnvelopeDocument FromDomain(PersistenceEnvelope<PlcTelemetryPayload> envelope)
        {
            return new PlcTelemetryEnvelopeDocument
            {
                RecordType = envelope.RecordType,
                SourceType = envelope.SourceType,
                SourceId = envelope.SourceId,
                TimestampUtc = envelope.TimestampUtc.UtcDateTime,
                IngestedAtUtc = envelope.IngestedAtUtc.UtcDateTime,
                Payload = PlcTelemetryPayloadDocument.FromDomain(envelope.Payload)
            };
        }

        public PlcTelemetrySample ToDomain()
        {
            return new PlcTelemetrySample(
                PlcId: SourceId,
                Payload: Payload.ToDomain().Variables,
                SourceTimestampUtc: new DateTimeOffset(DateTime.SpecifyKind(TimestampUtc, DateTimeKind.Utc)),
                IngestedAtUtc: new DateTimeOffset(DateTime.SpecifyKind(IngestedAtUtc, DateTimeKind.Utc)));
        }
    }

    private sealed class PlcTelemetryPayloadDocument
    {
        [BsonElement("variables")]
        [BsonDictionaryOptions(DictionaryRepresentation.Document)]
        public Dictionary<string, PlcTelemetryValueDocument> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [BsonElement("axis_position")]
        [BsonIgnoreIfNull]
        public double? LegacyAxisPosition { get; set; }

        [BsonElement("machine_status")]
        [BsonIgnoreIfNull]
        public string? LegacyMachineStatus { get; set; }

        [BsonElement("alarm_code")]
        [BsonIgnoreIfNull]
        public int? LegacyAlarmCode { get; set; }

        public static PlcTelemetryPayloadDocument FromDomain(PlcTelemetryPayload payload)
        {
            return new PlcTelemetryPayloadDocument
            {
                Variables = payload.Variables.ToDictionary(
                    item => item.Key,
                    item => PlcTelemetryValueDocument.FromDomain(item.Value),
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        public PlcTelemetryPayload ToDomain()
        {
            if (Variables.Count == 0)
            {
                var legacyVariables = new Dictionary<string, PlcTelemetryValue>(StringComparer.OrdinalIgnoreCase);

                if (LegacyAxisPosition.HasValue)
                {
                    legacyVariables["axis_position"] = new PlcTelemetryValue(
                        LegacyAxisPosition.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        LegacyAxisPosition.Value,
                        "number");
                }

                if (!string.IsNullOrWhiteSpace(LegacyMachineStatus))
                {
                    legacyVariables["machine_status"] = new PlcTelemetryValue(
                        LegacyMachineStatus,
                        bool.TryParse(LegacyMachineStatus, out var boolValue) ? (boolValue ? 1d : 0d) : null,
                        bool.TryParse(LegacyMachineStatus, out _) ? "boolean" : "string");
                }

                if (LegacyAlarmCode.HasValue)
                {
                    legacyVariables["alarm_code"] = new PlcTelemetryValue(
                        LegacyAlarmCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        LegacyAlarmCode.Value,
                        "number");
                }

                return new PlcTelemetryPayload(legacyVariables);
            }

            return new PlcTelemetryPayload(
                Variables.ToDictionary(
                    item => item.Key,
                    item => item.Value.ToDomain(),
                    StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class PlcTelemetryValueDocument
    {
        [BsonElement("raw_value")]
        public string RawValue { get; set; } = string.Empty;

        [BsonElement("numeric_value")]
        [BsonIgnoreIfNull]
        public double? NumericValue { get; set; }

        [BsonElement("value_type")]
        public string ValueType { get; set; } = "string";

        public static PlcTelemetryValueDocument FromDomain(PlcTelemetryValue value)
        {
            return new PlcTelemetryValueDocument
            {
                RawValue = value.RawValue,
                NumericValue = value.NumericValue,
                ValueType = value.ValueType
            };
        }

        public PlcTelemetryValue ToDomain()
        {
            return new PlcTelemetryValue(RawValue, NumericValue, ValueType);
        }
    }
}

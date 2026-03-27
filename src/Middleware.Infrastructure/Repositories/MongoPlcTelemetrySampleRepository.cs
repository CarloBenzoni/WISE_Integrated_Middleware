using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
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
        await collection.InsertOneAsync(PlcTelemetryEnvelopeDocument.FromDomain(sample.ToEnvelope()), cancellationToken: cancellationToken);
    }

    public async Task<PlcTelemetrySample?> GetLatestAsync(CancellationToken cancellationToken)
    {
        var document = await collection
            .Find(FilterDefinition<PlcTelemetryEnvelopeDocument>.Empty)
            .SortByDescending(item => item.TimestampUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDomain();
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
                AxisPosition: Payload.AxisPosition,
                MachineStatus: Payload.MachineStatus,
                AlarmCode: Payload.AlarmCode,
                SourceTimestampUtc: new DateTimeOffset(DateTime.SpecifyKind(TimestampUtc, DateTimeKind.Utc)),
                IngestedAtUtc: new DateTimeOffset(DateTime.SpecifyKind(IngestedAtUtc, DateTimeKind.Utc)));
        }
    }

    private sealed class PlcTelemetryPayloadDocument
    {
        [BsonElement("axis_position")]
        public double AxisPosition { get; set; }

        [BsonElement("machine_status")]
        public string MachineStatus { get; set; } = string.Empty;

        [BsonElement("alarm_code")]
        public int AlarmCode { get; set; }

        public static PlcTelemetryPayloadDocument FromDomain(PlcTelemetryPayload payload)
        {
            return new PlcTelemetryPayloadDocument
            {
                AxisPosition = payload.AxisPosition,
                MachineStatus = payload.MachineStatus,
                AlarmCode = payload.AlarmCode
            };
        }
    }
}

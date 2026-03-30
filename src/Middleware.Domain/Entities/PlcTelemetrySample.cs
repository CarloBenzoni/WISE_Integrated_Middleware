namespace Middleware.Domain.Entities;

public sealed record PlcTelemetrySample(
    string PlcId,
    IReadOnlyDictionary<string, PlcTelemetryValue> Payload,
    DateTimeOffset SourceTimestampUtc,
    DateTimeOffset IngestedAtUtc)
{
    public PersistenceEnvelope<PlcTelemetryPayload> ToEnvelope()
    {
        return new PersistenceEnvelope<PlcTelemetryPayload>(
            RecordType: "plc_telemetry_sample",
            SourceType: "plc",
            SourceId: PlcId,
            TimestampUtc: SourceTimestampUtc,
            IngestedAtUtc: IngestedAtUtc,
            Payload: new PlcTelemetryPayload(
                new Dictionary<string, PlcTelemetryValue>(Payload, StringComparer.OrdinalIgnoreCase)));
    }
}

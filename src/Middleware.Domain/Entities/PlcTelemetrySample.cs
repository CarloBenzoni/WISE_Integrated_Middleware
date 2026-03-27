namespace Middleware.Domain.Entities;

public sealed record PlcTelemetrySample(
    string PlcId,
    double AxisPosition,
    string MachineStatus,
    int AlarmCode,
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
                AxisPosition,
                MachineStatus,
                AlarmCode));
    }
}

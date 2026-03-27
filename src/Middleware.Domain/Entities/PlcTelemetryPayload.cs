namespace Middleware.Domain.Entities;

public sealed record PlcTelemetryPayload(
    double AxisPosition,
    string MachineStatus,
    int AlarmCode);

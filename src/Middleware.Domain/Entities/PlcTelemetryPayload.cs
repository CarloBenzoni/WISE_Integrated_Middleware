namespace Middleware.Domain.Entities;

public sealed record PlcTelemetryPayload(
    IReadOnlyDictionary<string, PlcTelemetryValue> Variables);

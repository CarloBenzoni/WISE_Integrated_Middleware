namespace Middleware.Domain.Entities;

public sealed record TwinCatTelemetrySample(
    string SourceId,
    string GroupId,
    IReadOnlyDictionary<string, PlcTelemetryValue> Payload,
    IReadOnlyDictionary<string, string> ReadErrors,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset IngestedAtUtc);

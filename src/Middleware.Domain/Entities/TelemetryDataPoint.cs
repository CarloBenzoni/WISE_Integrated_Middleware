namespace Middleware.Domain.Entities;

public sealed record TelemetryDataPoint(
    DateTimeOffset TimestampUtc,
    string RawValue,
    double? NumericValue);

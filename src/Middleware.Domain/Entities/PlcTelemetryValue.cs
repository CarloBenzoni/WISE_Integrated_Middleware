namespace Middleware.Domain.Entities;

public sealed record PlcTelemetryValue(
    string RawValue,
    double? NumericValue,
    string ValueType);

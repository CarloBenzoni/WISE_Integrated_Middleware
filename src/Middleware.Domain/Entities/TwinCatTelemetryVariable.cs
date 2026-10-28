namespace Middleware.Domain.Entities;

public sealed record TwinCatTelemetryVariable(
    string Key,
    string Symbol,
    string DataType);

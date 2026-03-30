namespace Middleware.Domain.Entities;

public sealed record TelemetryVariableDefinition(
    string Key,
    string DisplayName,
    string ValueType);

namespace Middleware.Domain.Entities;

public sealed record ProductionParameterSnapshot(
    string Key,
    string Label,
    string? Unit,
    string Value);

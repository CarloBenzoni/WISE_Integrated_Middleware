namespace Middleware.Domain.Entities;

public sealed record ProductionJobQuery(
    string? Status,
    string? DesignId,
    string? UseCase,
    string? RequestedBy);

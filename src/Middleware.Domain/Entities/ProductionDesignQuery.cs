namespace Middleware.Domain.Entities;

public sealed record ProductionDesignQuery(
    string? Search,
    string? UseCase,
    string? DesignedBy,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);

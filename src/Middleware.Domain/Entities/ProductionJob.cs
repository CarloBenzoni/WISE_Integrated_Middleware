namespace Middleware.Domain.Entities;

public sealed record ProductionJob(
    string JobId,
    string DesignId,
    string DesignName,
    string UseCase,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string Status,
    int? CurrentStepSequence,
    string? CurrentStepName,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureReason,
    IReadOnlyCollection<ProductionJobEvent> Events);

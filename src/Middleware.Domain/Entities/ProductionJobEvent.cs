namespace Middleware.Domain.Entities;

public sealed record ProductionJobEvent(
    DateTimeOffset OccurredAtUtc,
    string Level,
    string Message,
    int? StepSequence,
    string? StepName);

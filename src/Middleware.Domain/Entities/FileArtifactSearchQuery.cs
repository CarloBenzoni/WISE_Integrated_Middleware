namespace Middleware.Domain.Entities;

public sealed record FileArtifactSearchQuery(
    string? Search,
    string? UseCase,
    string? Category,
    string? UploadedBy,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);

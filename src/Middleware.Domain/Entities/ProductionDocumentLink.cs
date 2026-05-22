namespace Middleware.Domain.Entities;

public sealed record ProductionDocumentLink(
    string DocumentId,
    string Role,
    string? Category,
    string? FileName);

namespace Middleware.Domain.Entities;

public sealed record FileArtifactUploadRequest(
    string LocalFilePath,
    string OriginalFileName,
    string UploadedBy,
    string MimeType,
    string? UseCase,
    string? Category,
    string SourceSystem);

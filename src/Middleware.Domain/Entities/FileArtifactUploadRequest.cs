namespace Middleware.Domain.Entities;

public sealed record FileArtifactUploadRequest(
    string LocalFilePath,
    string OriginalFileName,
    string UploadedBy,
    string MimeType,
    string? Category,
    string SourceSystem);

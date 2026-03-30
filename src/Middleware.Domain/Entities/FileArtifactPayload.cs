namespace Middleware.Domain.Entities;

public sealed record FileArtifactPayload(
    string DocumentId,
    string FileName,
    string OriginalFileName,
    string LocalPath,
    string StorageType,
    string StoragePath,
    long SizeBytes,
    string UploadedBy,
    string FileType,
    string MimeType,
    string? UseCase,
    string? Category,
    string SourceSystem);

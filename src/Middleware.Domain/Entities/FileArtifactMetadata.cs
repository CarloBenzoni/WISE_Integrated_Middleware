namespace Middleware.Domain.Entities;

public sealed record FileArtifactMetadata(
    string DocumentId,
    string FileName,
    string OriginalFileName,
    string LocalPath,
    string StoragePath,
    long Size,
    string UserName,
    string FileType,
    string MimeType,
    string? UseCase,
    string? Category,
    string SourceSystem,
    DateTimeOffset UploadDateUtc)
{
    public PersistenceEnvelope<FileArtifactPayload> ToEnvelope()
    {
        return new PersistenceEnvelope<FileArtifactPayload>(
            RecordType: "document_repository_artifact",
            SourceType: SourceSystem,
            SourceId: "document_repository",
            TimestampUtc: UploadDateUtc,
            IngestedAtUtc: UploadDateUtc,
            Payload: new FileArtifactPayload(
                DocumentId,
                FileName,
                OriginalFileName,
                LocalPath,
                "hdfs",
                StoragePath,
                Size,
                UserName,
                FileType,
                MimeType,
                UseCase,
                Category,
                SourceSystem));
    }
}

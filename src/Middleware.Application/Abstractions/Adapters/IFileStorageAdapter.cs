using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Adapters;

public interface IFileStorageAdapter
{
    Task<FileArtifactMetadata> UploadFileAsync(FileArtifactUploadRequest request, CancellationToken cancellationToken);
    Task<StoredFileContent> DownloadFileAsync(FileArtifactMetadata metadata, CancellationToken cancellationToken);
    Task DeleteFileAsync(FileArtifactMetadata metadata, CancellationToken cancellationToken);
}

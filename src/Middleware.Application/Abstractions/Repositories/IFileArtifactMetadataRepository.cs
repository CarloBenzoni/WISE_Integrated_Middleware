using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Repositories;

public interface IFileArtifactMetadataRepository
{
    Task InsertAsync(FileArtifactMetadata metadata, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FileArtifactMetadata>> ListAsync(CancellationToken cancellationToken);
    Task<FileArtifactMetadata?> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken);
}

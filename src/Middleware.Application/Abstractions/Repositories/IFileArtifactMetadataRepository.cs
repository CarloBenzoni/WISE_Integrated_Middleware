using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Repositories;

public interface IFileArtifactMetadataRepository
{
    Task InsertAsync(FileArtifactMetadata metadata, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FileArtifactMetadata>> ListAsync(FileArtifactSearchQuery query, CancellationToken cancellationToken);
    Task<FileArtifactMetadata?> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken);
}

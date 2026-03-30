using Middleware.Application.Abstractions.Adapters;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;

namespace Middleware.Application.Services;

public sealed class FileUploadService(
    IFileStorageAdapter fileStorageAdapter,
    IFileArtifactMetadataRepository metadataRepository)
{
    public async Task<IReadOnlyCollection<FileArtifactMetadata>> UploadFilesAsync(
        IEnumerable<FileArtifactUploadRequest> requests,
        CancellationToken cancellationToken)
    {
        var uploadedFiles = new List<FileArtifactMetadata>();

        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metadata = await fileStorageAdapter.UploadFileAsync(request, cancellationToken);
            await metadataRepository.InsertAsync(metadata, cancellationToken);
            uploadedFiles.Add(metadata);
        }

        return uploadedFiles;
    }

    public async Task<IReadOnlyCollection<FileArtifactMetadata>> UploadFolderAsync(
        string folderPath,
        string userName,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Upload folder not found: {folderPath}");
        }

        var files = Directory.GetFiles(folderPath);
        var requests = files.Select(file => new FileArtifactUploadRequest(
            LocalFilePath: file,
            OriginalFileName: Path.GetFileName(file),
            UploadedBy: userName,
            MimeType: "application/octet-stream",
            UseCase: "General",
            Category: "batch-upload",
            SourceSystem: "worker"));

        return await UploadFilesAsync(requests, cancellationToken);
    }

    public Task<IReadOnlyCollection<FileArtifactMetadata>> ListAsync(FileArtifactSearchQuery query, CancellationToken cancellationToken)
    {
        return metadataRepository.ListAsync(query, cancellationToken);
    }

    public Task<FileArtifactMetadata?> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken)
    {
        return metadataRepository.GetByDocumentIdAsync(documentId, cancellationToken);
    }

    public async Task<StoredFileContent> DownloadAsync(string documentId, CancellationToken cancellationToken)
    {
        var metadata = await metadataRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (metadata is null)
        {
            throw new FileNotFoundException($"Document not found: {documentId}");
        }

        return await fileStorageAdapter.DownloadFileAsync(metadata, cancellationToken);
    }

    public async Task<IReadOnlyCollection<StoredFileContent>> DownloadManyAsync(
        IEnumerable<string> documentIds,
        CancellationToken cancellationToken)
    {
        var files = new List<StoredFileContent>();

        foreach (var documentId in documentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            files.Add(await DownloadAsync(documentId, cancellationToken));
        }

        return files;
    }

    public async Task<bool> DeleteAsync(string documentId, CancellationToken cancellationToken)
    {
        var metadata = await metadataRepository.GetByDocumentIdAsync(documentId, cancellationToken);
        if (metadata is null)
        {
            return false;
        }

        await fileStorageAdapter.DeleteFileAsync(metadata, cancellationToken);
        return await metadataRepository.DeleteAsync(documentId, cancellationToken);
    }
}

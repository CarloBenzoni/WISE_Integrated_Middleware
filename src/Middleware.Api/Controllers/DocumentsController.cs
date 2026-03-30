using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Services;
using Middleware.Domain.Entities;

namespace Middleware.Api.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(FileUploadService fileUploadService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? useCase,
        [FromQuery] string? category,
        [FromQuery] string? uploadedBy,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var documents = await fileUploadService.ListAsync(
            new FileArtifactSearchQuery(search, useCase, category, uploadedBy, fromUtc, toUtc),
            cancellationToken);
        return Ok(documents.Select(MapResponse));
    }

    [HttpGet("{documentId}")]
    public async Task<IActionResult> GetById(string documentId, CancellationToken cancellationToken)
    {
        var document = await fileUploadService.GetByDocumentIdAsync(documentId, cancellationToken);
        if (document is null)
        {
            return NotFound(new { message = $"Document not found: {documentId}" });
        }

        return Ok(MapResponse(document));
    }

    [HttpGet("{documentId}/download")]
    public async Task<IActionResult> Download(string documentId, CancellationToken cancellationToken)
    {
        try
        {
            var file = await fileUploadService.DownloadAsync(documentId, cancellationToken);
            return File(file.Content, file.MimeType, file.FileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = $"Document not found: {documentId}" });
        }
    }

    [HttpDelete("{documentId}")]
    public async Task<IActionResult> Delete(string documentId, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await fileUploadService.DeleteAsync(documentId, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { message = $"Document not found: {documentId}" });
            }

            return NoContent();
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = $"Document not found: {documentId}" });
        }
    }

    [HttpPost("download-zip")]
    public async Task<IActionResult> DownloadZip(
        [FromBody] DocumentZipDownloadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DocumentIds is null || request.DocumentIds.Count == 0)
        {
            return BadRequest(new { message = "At least one documentId is required." });
        }

        var files = await fileUploadService.DownloadManyAsync(request.DocumentIds, cancellationToken);
        if (files.Count == 0)
        {
            return NotFound(new { message = "No documents found for zip download." });
        }

        await using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.FileName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(file.Content, cancellationToken);
            }
        }

        memoryStream.Position = 0;
        var fileName = $"wise_documents_{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
        return File(memoryStream.ToArray(), "application/zip", fileName);
    }

    [HttpPost("upload")]
    [RequestFormLimits(MultipartBodyLengthLimit = 268435456)]
    [RequestSizeLimit(268435456)]
    public async Task<IActionResult> Upload(
        [FromForm] DocumentUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Files is null || request.Files.Count == 0)
        {
            return BadRequest(new { message = "At least one file is required." });
        }

        if (string.IsNullOrWhiteSpace(request.UploadedBy))
        {
            return BadRequest(new { message = "uploadedBy is required." });
        }

        if (string.IsNullOrWhiteSpace(request.UseCase))
        {
            return BadRequest(new { message = "useCase is required." });
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "wise-document-repository");
        Directory.CreateDirectory(tempDirectory);

        var tempFiles = new List<string>();

        try
        {
            var uploadRequests = new List<FileArtifactUploadRequest>();

            foreach (var file in request.Files)
            {
                if (file.Length == 0)
                {
                    continue;
                }

                var tempFilePath = Path.Combine(
                    tempDirectory,
                    $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}");

                await using (var stream = System.IO.File.Create(tempFilePath))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }

                tempFiles.Add(tempFilePath);
                uploadRequests.Add(new FileArtifactUploadRequest(
                    LocalFilePath: tempFilePath,
                    OriginalFileName: file.FileName,
                    UploadedBy: request.UploadedBy,
                    MimeType: file.ContentType,
                    UseCase: request.UseCase,
                    Category: request.Category,
                    SourceSystem: string.IsNullOrWhiteSpace(request.SourceSystem)
                        ? "hmi_wise_ddg"
                        : request.SourceSystem));
            }

            var documents = await fileUploadService.UploadFilesAsync(uploadRequests, cancellationToken);
            return Ok(documents.Select(MapResponse));
        }
        finally
        {
            foreach (var tempFile in tempFiles)
            {
                if (System.IO.File.Exists(tempFile))
                {
                    System.IO.File.Delete(tempFile);
                }
            }
        }
    }

    private static DocumentResponse MapResponse(FileArtifactMetadata metadata)
    {
        return new DocumentResponse(
            metadata.DocumentId,
            metadata.FileName,
            metadata.OriginalFileName,
            metadata.StoragePath,
            metadata.Size,
            metadata.FileType,
            metadata.MimeType,
            metadata.UseCase,
            metadata.Category,
            metadata.SourceSystem,
            metadata.UserName,
            metadata.UploadDateUtc);
    }
}

public sealed record DocumentUploadRequest(
    string UploadedBy,
    string UseCase,
    string? Category,
    string? SourceSystem,
    List<IFormFile> Files);

public sealed record DocumentZipDownloadRequest(List<string> DocumentIds);

public sealed record DocumentResponse(
    string DocumentId,
    string FileName,
    string OriginalFileName,
    string StoragePath,
    long Size,
    string FileType,
    string MimeType,
    string? UseCase,
    string? Category,
    string SourceSystem,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc);

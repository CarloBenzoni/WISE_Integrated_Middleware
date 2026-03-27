using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Infrastructure.Adapters;

public sealed class WebHdfsFileStorageAdapter(
    IHttpClientFactory httpClientFactory,
    ILogger<WebHdfsFileStorageAdapter> logger,
    IOptions<MiddlewareOptions> options) : IFileStorageAdapter
{
    public async Task<FileArtifactMetadata> UploadFileAsync(
        FileArtifactUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.LocalFilePath))
        {
            throw new FileNotFoundException("Local file not found.", request.LocalFilePath);
        }

        var storageOptions = options.Value.Storage;
        var fileInfo = new FileInfo(request.LocalFilePath);
        var documentId = Guid.NewGuid().ToString("N");
        var storedFileName = BuildStoredFileName(documentId, request.OriginalFileName);
        var hdfsPath = CombineHdfsPath(storageOptions.HdfsFolderPath, storedFileName);
        var client = httpClientFactory.CreateClient("webhdfs");

        if (HasDirectDataNodeUpload(storageOptions))
        {
            var uploadUri = BuildDirectDataNodeCreateUri(storageOptions, hdfsPath, overwrite: false);
            logger.LogInformation("WebHDFS direct upload request for {OriginalFileName}: {UploadUri}", request.OriginalFileName, uploadUri);
            await using var fileStream = File.OpenRead(request.LocalFilePath);
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUri)
            {
                Content = new StreamContent(fileStream)
            };

            using var uploadResponse = await client.SendAsync(uploadRequest, cancellationToken);
            uploadResponse.EnsureSuccessStatusCode();
        }
        else
        {
            var createUri = BuildCreateUri(storageOptions, hdfsPath, overwrite: false);
            logger.LogInformation("WebHDFS create request for {OriginalFileName}: {CreateUri}", request.OriginalFileName, createUri);
            using var createRequest = new HttpRequestMessage(HttpMethod.Put, createUri);
            using var createResponse = await client.SendAsync(
                createRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (createResponse.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect ||
                createResponse.StatusCode == System.Net.HttpStatusCode.Created)
            {
                logger.LogInformation(
                    "WebHDFS raw create response for {OriginalFileName}: StatusCode={StatusCode}, Location={Location}",
                    request.OriginalFileName,
                    (int)createResponse.StatusCode,
                    createResponse.Headers.Location?.ToString() ?? "<null>");

                var uploadUri = NormalizeRedirectUri(createResponse.Headers.Location, createUri);
                logger.LogInformation("WebHDFS upload target for {OriginalFileName}: {UploadUri}", request.OriginalFileName, uploadUri);
                await using var fileStream = File.OpenRead(request.LocalFilePath);
                using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUri)
                {
                    Content = new StreamContent(fileStream)
                };

                using var uploadResponse = await client.SendAsync(uploadRequest, cancellationToken);
                uploadResponse.EnsureSuccessStatusCode();
            }
            else
            {
                var body = await createResponse.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"WebHDFS create failed for {hdfsPath}: {(int)createResponse.StatusCode} {body}");
            }
        }

        return new FileArtifactMetadata(
            DocumentId: documentId,
            FileName: storedFileName,
            OriginalFileName: request.OriginalFileName,
            LocalPath: fileInfo.FullName,
            StoragePath: hdfsPath,
            Size: fileInfo.Length,
            UserName: request.UploadedBy,
            FileType: fileInfo.Extension,
            MimeType: string.IsNullOrWhiteSpace(request.MimeType) ? "application/octet-stream" : request.MimeType,
            Category: string.IsNullOrWhiteSpace(request.Category) ? null : request.Category,
            SourceSystem: string.IsNullOrWhiteSpace(request.SourceSystem) ? "unknown" : request.SourceSystem,
            UploadDateUtc: DateTimeOffset.UtcNow);
    }

    public async Task<StoredFileContent> DownloadFileAsync(
        FileArtifactMetadata metadata,
        CancellationToken cancellationToken)
    {
        var storageOptions = options.Value.Storage;
        var client = httpClientFactory.CreateClient("webhdfs");
        var openUri = HasDirectDataNodeUpload(storageOptions)
            ? BuildDirectDataNodeOpenUri(storageOptions, metadata.StoragePath)
            : BuildOpenUri(storageOptions, metadata.StoragePath);
        logger.LogInformation("WebHDFS open request for {DocumentId}: {OpenUri}", metadata.DocumentId, openUri);

        using var response = await client.GetAsync(openUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect && response.Headers.Location is not null)
        {
            var downloadUri = NormalizeRedirectUri(response.Headers.Location, openUri);
            logger.LogInformation("WebHDFS download target for {DocumentId}: {DownloadUri}", metadata.DocumentId, downloadUri);
            using var redirectedResponse = await client.GetAsync(downloadUri, cancellationToken);
            redirectedResponse.EnsureSuccessStatusCode();

            var redirectedContent = await redirectedResponse.Content.ReadAsByteArrayAsync(cancellationToken);
            return new StoredFileContent(
                FileName: metadata.OriginalFileName,
                MimeType: string.IsNullOrWhiteSpace(metadata.MimeType) ? "application/octet-stream" : metadata.MimeType,
                Content: redirectedContent);
        }

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new StoredFileContent(
            FileName: metadata.OriginalFileName,
            MimeType: string.IsNullOrWhiteSpace(metadata.MimeType) ? "application/octet-stream" : metadata.MimeType,
            Content: content);
    }

    private static Uri BuildCreateUri(StorageOptions options, string hdfsPath, bool overwrite)
    {
        var normalizedBaseUrl = NormalizeWebHdfsBaseUrl(options.WebHdfsBaseUrl);
        var normalizedPath = hdfsPath.TrimStart('/');
        var uri = $"{normalizedBaseUrl}/webhdfs/v1/{normalizedPath}?op=CREATE&user.name={Uri.EscapeDataString(options.HdfsUser)}&overwrite={overwrite.ToString().ToLowerInvariant()}";
        return new Uri(uri);
    }

    private static Uri BuildOpenUri(StorageOptions options, string hdfsPath)
    {
        var normalizedBaseUrl = NormalizeWebHdfsBaseUrl(options.WebHdfsBaseUrl);
        var normalizedPath = hdfsPath.TrimStart('/');
        var uri = $"{normalizedBaseUrl}/webhdfs/v1/{normalizedPath}?op=OPEN&user.name={Uri.EscapeDataString(options.HdfsUser)}";
        return new Uri(uri);
    }

    private static Uri BuildDirectDataNodeCreateUri(StorageOptions options, string hdfsPath, bool overwrite)
    {
        var normalizedBaseUrl = NormalizeWebHdfsBaseUrl(options.DataNodeWebHdfsBaseUrl);
        var normalizedPath = hdfsPath.TrimStart('/');
        var uri =
            $"{normalizedBaseUrl}/webhdfs/v1/{normalizedPath}?op=CREATE&user.name={Uri.EscapeDataString(options.HdfsUser)}" +
            $"&namenoderpcaddress={Uri.EscapeDataString(options.NameNodeRpcAddress)}&createflag=&createparent=true&overwrite={overwrite.ToString().ToLowerInvariant()}";
        return new Uri(uri);
    }

    private static Uri BuildDirectDataNodeOpenUri(StorageOptions options, string hdfsPath)
    {
        var normalizedBaseUrl = NormalizeWebHdfsBaseUrl(options.DataNodeWebHdfsBaseUrl);
        var normalizedPath = hdfsPath.TrimStart('/');
        var uri =
            $"{normalizedBaseUrl}/webhdfs/v1/{normalizedPath}?op=OPEN&user.name={Uri.EscapeDataString(options.HdfsUser)}" +
            $"&namenoderpcaddress={Uri.EscapeDataString(options.NameNodeRpcAddress)}";
        return new Uri(uri);
    }

    private static string NormalizeWebHdfsBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Storage.WebHdfsBaseUrl is required.");
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid WebHDFS base URL: {baseUrl}");
        }

        if (uri.Scheme.Equals("hdfs", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttp
            };

            if (builder.Port <= 0)
            {
                builder.Port = 9870;
            }

            return builder.Uri.ToString().TrimEnd('/');
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Unsupported WebHDFS base URL scheme '{uri.Scheme}'. Use http, https or hdfs.");
        }

        return trimmed;
    }

    private static Uri NormalizeRedirectUri(Uri? redirectUri, Uri fallbackUri)
    {
        if (redirectUri is null)
        {
            return fallbackUri;
        }

        if (!redirectUri.IsAbsoluteUri)
        {
            return EnsureWebHdfsPath(new Uri(fallbackUri, redirectUri));
        }

        if (redirectUri.Scheme.Equals("hdfs", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(redirectUri)
            {
                Scheme = Uri.UriSchemeHttp
            };

            if (builder.Port <= 0)
            {
                builder.Port = 9870;
            }

            return EnsureWebHdfsPath(builder.Uri);
        }

        return EnsureWebHdfsPath(redirectUri);
    }

    private static Uri EnsureWebHdfsPath(Uri uri)
    {
        if (uri.AbsolutePath.StartsWith("/webhdfs/v1/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.AbsolutePath, "/webhdfs/v1", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var normalizedPath = builder.Path.StartsWith("/", StringComparison.Ordinal)
            ? builder.Path
            : $"/{builder.Path}";

        builder.Path = $"/webhdfs/v1{normalizedPath}";
        return builder.Uri;
    }

    private static bool HasDirectDataNodeUpload(StorageOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.DataNodeWebHdfsBaseUrl) &&
               !string.IsNullOrWhiteSpace(options.NameNodeRpcAddress);
    }

    private static string BuildStoredFileName(string documentId, string originalFileName)
    {
        var safeFileName = Path.GetFileName(originalFileName);
        return $"{documentId}_{safeFileName}";
    }

    private static string CombineHdfsPath(string folderPath, string fileName)
    {
        var normalizedFolder = folderPath.Replace("\\", "/").TrimEnd('/');
        return $"{normalizedFolder}/{fileName}";
    }
}

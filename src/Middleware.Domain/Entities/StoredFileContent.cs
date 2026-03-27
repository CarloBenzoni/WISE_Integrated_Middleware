namespace Middleware.Domain.Entities;

public sealed record StoredFileContent(
    string FileName,
    string MimeType,
    byte[] Content);

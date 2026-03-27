namespace Middleware.Domain.Entities;

public sealed record PolarisGCodeRunResult(
    string ServerIpAddress,
    string LocalFilePath,
    string RemoteFileName,
    string RemoteDirectory,
    DateTimeOffset ExecutedAtUtc);

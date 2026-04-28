namespace Middleware.Domain.Entities;

public sealed record PolarisGCodeStatusResult(
    string ServerIpAddress,
    int StateCode,
    string State,
    string? Error,
    DateTimeOffset ReadAtUtc);

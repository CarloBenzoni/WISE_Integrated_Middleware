namespace Middleware.Domain.Entities;

public sealed record PolarisAxisCommandResult(
    string ServerIpAddress,
    string AxisName,
    int AxisNumber,
    string Command,
    DateTimeOffset ExecutedAtUtc);

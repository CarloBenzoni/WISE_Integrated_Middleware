namespace Middleware.Domain.Entities;

public sealed record PolarisSystemInfo(
    string ServerIpAddress,
    string LinuxKernelVersion,
    string PcieDriverVersion,
    string PolarisCurrentStatus,
    int PolarisStartupStatus,
    DateTimeOffset ReadAtUtc);

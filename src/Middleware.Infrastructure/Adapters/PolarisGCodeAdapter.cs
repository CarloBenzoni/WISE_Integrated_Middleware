using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Infrastructure.Configuration;
using PolarisCommunication;
using PolarisCommunication.PolarisServerAPI;
using Middleware.Domain.Entities;

namespace Middleware.Infrastructure.Adapters;

public sealed class PolarisGCodeAdapter(IOptions<MiddlewareOptions> options) : IPolarisGCodeAdapter
{
    private readonly object syncRoot = new();
    private IScript? script;
    private IFileSystem? fileSystem;
    private string? connectedScriptIpAddress;
    private string? connectedFileSystemIpAddress;

    public Task UploadGCodeAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("Local G-code file not found.", localFilePath);
        }

        var polarisOptions = options.Value.Polaris;
        var connectedFileSystem = GetConnectedFileSystem(polarisOptions.IpAddress);

        lock (syncRoot)
        {
            connectedFileSystem.RemoteDirectory = polarisOptions.GCodeDirectory;
            connectedFileSystem.RemoteFile = remoteFileName;
            connectedFileSystem.UploadFile(localFilePath);
        }

        return Task.CompletedTask;
    }

    public Task RunRemoteGCodeAsync(string remoteFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectedScript = GetConnectedScript(options.Value.Polaris.IpAddress);

        lock (syncRoot)
        {
            GCI.RunGcode(connectedScript, remoteFileName);
        }

        return Task.CompletedTask;
    }

    public Task<PolarisSystemInfo> ReadSystemInfoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var polarisOptions = options.Value.Polaris;
        var connectedScript = GetConnectedScript(polarisOptions.IpAddress);

        PolarisSystemInfo systemInfo;

        lock (syncRoot)
        {
            systemInfo = new PolarisSystemInfo(
                ServerIpAddress: polarisOptions.IpAddress,
                LinuxKernelVersion: PSU.GetLinuxKernelVersion(connectedScript),
                PcieDriverVersion: PSU.GetPCIeDriverVersion(connectedScript),
                PolarisCurrentStatus: PSU.GetPolarisCurrentStatus(connectedScript).ToString(),
                PolarisStartupStatus: PSU.GetPolarisStartupStatus(connectedScript),
                ReadAtUtc: DateTimeOffset.UtcNow);
        }

        return Task.FromResult(systemInfo);
    }

    private IScript GetConnectedScript(string ipAddress)
    {
        lock (syncRoot)
        {
            script ??= Script.GetInstance();

            if (!string.Equals(connectedScriptIpAddress, ipAddress, StringComparison.OrdinalIgnoreCase))
            {
                script.Connect(ipAddress);
                connectedScriptIpAddress = ipAddress;
            }

            return script;
        }
    }

    private IFileSystem GetConnectedFileSystem(string ipAddress)
    {
        lock (syncRoot)
        {
            fileSystem ??= FileSystem.GetInstance();

            if (!string.Equals(connectedFileSystemIpAddress, ipAddress, StringComparison.OrdinalIgnoreCase))
            {
                fileSystem.Connect(ipAddress);
                connectedFileSystemIpAddress = ipAddress;
            }

            return fileSystem;
        }
    }
}

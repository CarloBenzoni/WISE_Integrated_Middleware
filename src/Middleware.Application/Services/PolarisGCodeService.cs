using Middleware.Application.Abstractions.Adapters;
using Middleware.Domain.Entities;

namespace Middleware.Application.Services;

public sealed class PolarisGCodeService(IPolarisGCodeAdapter polarisGCodeAdapter)
{
    public async Task UploadAsync(
        string localFilePath,
        string remoteFileName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new ArgumentException("Local file path is required.", nameof(localFilePath));
        }

        if (string.IsNullOrWhiteSpace(remoteFileName))
        {
            throw new ArgumentException("Remote file name is required.", nameof(remoteFileName));
        }

        await polarisGCodeAdapter.UploadGCodeAsync(localFilePath, remoteFileName, cancellationToken);
    }

    public async Task<PolarisGCodeRunResult> UploadAndRunAsync(
        string localFilePath,
        string remoteFileName,
        string serverIpAddress,
        string remoteDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new ArgumentException("Local file path is required.", nameof(localFilePath));
        }

        if (string.IsNullOrWhiteSpace(remoteFileName))
        {
            throw new ArgumentException("Remote file name is required.", nameof(remoteFileName));
        }

        await polarisGCodeAdapter.UploadGCodeAsync(localFilePath, remoteFileName, cancellationToken);
        await polarisGCodeAdapter.RunRemoteGCodeAsync(remoteFileName, cancellationToken);

        return new PolarisGCodeRunResult(
            ServerIpAddress: serverIpAddress,
            LocalFilePath: localFilePath,
            RemoteFileName: remoteFileName,
            RemoteDirectory: remoteDirectory,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }

    public async Task<PolarisGCodeRunResult> RunRemoteAsync(
        string remoteFileName,
        string serverIpAddress,
        string remoteDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteFileName))
        {
            throw new ArgumentException("Remote file name is required.", nameof(remoteFileName));
        }

        await polarisGCodeAdapter.RunRemoteGCodeAsync(remoteFileName, cancellationToken);

        return new PolarisGCodeRunResult(
            ServerIpAddress: serverIpAddress,
            LocalFilePath: string.Empty,
            RemoteFileName: remoteFileName,
            RemoteDirectory: remoteDirectory,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }

    public async Task<PolarisAxisCommandResult> EnableAxisAsync(
        string axisName,
        string serverIpAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(axisName))
        {
            throw new ArgumentException("Axis name is required.", nameof(axisName));
        }

        var axisNumber = await polarisGCodeAdapter.EnableAxisAsync(axisName, cancellationToken);

        return new PolarisAxisCommandResult(
            ServerIpAddress: serverIpAddress,
            AxisName: axisName,
            AxisNumber: axisNumber,
            Command: "enable-axis",
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }

    public Task<PolarisGCodeStatusResult> ReadStatusAsync(CancellationToken cancellationToken)
    {
        return polarisGCodeAdapter.ReadGCodeStatusAsync(cancellationToken);
    }
}

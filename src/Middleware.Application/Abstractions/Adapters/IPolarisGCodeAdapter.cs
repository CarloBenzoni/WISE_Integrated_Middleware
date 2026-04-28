namespace Middleware.Application.Abstractions.Adapters;

public interface IPolarisGCodeAdapter
{
    Task UploadGCodeAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken);
    Task RunRemoteGCodeAsync(string remoteFileName, CancellationToken cancellationToken);
    Task<int> EnableAxisAsync(string axisName, CancellationToken cancellationToken);
    Task<Middleware.Domain.Entities.PolarisGCodeStatusResult> ReadGCodeStatusAsync(CancellationToken cancellationToken);
    Task<Middleware.Domain.Entities.PolarisSystemInfo> ReadSystemInfoAsync(CancellationToken cancellationToken);
}

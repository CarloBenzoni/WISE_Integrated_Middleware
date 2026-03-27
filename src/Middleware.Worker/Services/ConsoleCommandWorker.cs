using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Services;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Worker.Services;

public sealed class ConsoleCommandWorker(
    ILogger<ConsoleCommandWorker> logger,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<MiddlewareOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Console commands enabled. Use 'upload-files <username>', 'polaris-run <localFileNameOrPath> [remoteFileName]', 'polaris-run-remote <remoteFileName>' or 'polaris-info'.");

        while (!stoppingToken.IsCancellationRequested)
        {
            string? input;

            try
            {
                input = await Task.Run(Console.ReadLine, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts[0].Equals("upload-files", StringComparison.OrdinalIgnoreCase))
            {
                var userName = parts.Length > 1 ? parts[1] : Environment.UserName;
                var folderPath = options.Value.Storage.UploadFolderPath;

                try
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var fileUploadService = scope.ServiceProvider.GetRequiredService<FileUploadService>();
                    var uploadedFiles = await fileUploadService.UploadFolderAsync(folderPath, userName, stoppingToken);

                    logger.LogInformation(
                        "File upload completed. Uploaded {Count} files from {FolderPath} as user {UserName}.",
                        uploadedFiles.Count,
                        folderPath,
                        userName);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "File upload command failed.");
                }

                continue;
            }

            if (parts[0].Equals("polaris-run", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    logger.LogWarning("Usage: polaris-run <localFileNameOrPath> [remoteFileName]");
                    continue;
                }

                var localFilePath = ResolvePolarisLocalFilePath(parts[1], options.Value.Polaris.LocalGCodeFolderPath);
                var remoteFileName = parts.Length > 2 ? parts[2] : Path.GetFileName(localFilePath);

                try
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var polarisGCodeService = scope.ServiceProvider.GetRequiredService<PolarisGCodeService>();
                    var result = await polarisGCodeService.UploadAndRunAsync(
                        localFilePath,
                        remoteFileName,
                        options.Value.Polaris.IpAddress,
                        options.Value.Polaris.GCodeDirectory,
                        stoppingToken);

                    logger.LogInformation(
                        "Polaris G-code started. Server {ServerIpAddress}, remote file {RemoteFileName}, directory {RemoteDirectory}.",
                        result.ServerIpAddress,
                        result.RemoteFileName,
                        result.RemoteDirectory);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Polaris upload-and-run command failed.");
                }

                continue;
            }

            if (parts[0].Equals("polaris-run-remote", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    logger.LogWarning("Usage: polaris-run-remote <remoteFileName>");
                    continue;
                }

                try
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var polarisGCodeService = scope.ServiceProvider.GetRequiredService<PolarisGCodeService>();
                    var result = await polarisGCodeService.RunRemoteAsync(
                        parts[1],
                        options.Value.Polaris.IpAddress,
                        options.Value.Polaris.GCodeDirectory,
                        stoppingToken);

                    logger.LogInformation(
                        "Polaris remote G-code started. Server {ServerIpAddress}, remote file {RemoteFileName}.",
                        result.ServerIpAddress,
                        result.RemoteFileName);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Polaris remote run command failed.");
                }

                continue;
            }

            if (parts[0].Equals("polaris-info", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var polarisInfoService = scope.ServiceProvider.GetRequiredService<PolarisInfoService>();
                    var result = await polarisInfoService.ReadSystemInfoAsync(stoppingToken);

                    logger.LogInformation(
                        "Polaris info. Server {ServerIpAddress}, LinuxKernelVersion {LinuxKernelVersion}, PCIeDriverVersion {PcieDriverVersion}, CurrentStatus {PolarisCurrentStatus}, StartupStatus {PolarisStartupStatus}.",
                        result.ServerIpAddress,
                        result.LinuxKernelVersion,
                        result.PcieDriverVersion,
                        result.PolarisCurrentStatus,
                        result.PolarisStartupStatus);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Polaris info command failed.");
                }

                continue;
            }

            logger.LogWarning("Unknown command: {Command}", input);
        }
    }

    private static string ResolvePolarisLocalFilePath(string fileNameOrPath, string configuredFolderPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
        {
            throw new ArgumentException("A local file name or path is required.", nameof(fileNameOrPath));
        }

        return Path.IsPathRooted(fileNameOrPath)
            ? fileNameOrPath
            : Path.Combine(configuredFolderPath, fileNameOrPath);
    }
}

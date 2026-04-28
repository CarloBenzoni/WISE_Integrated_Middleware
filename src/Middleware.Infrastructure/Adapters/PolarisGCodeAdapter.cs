using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using PolarisCommunication;
using PolarisCommunication.PolarisServerAPI;
using Middleware.Domain.Entities;

namespace Middleware.Infrastructure.Adapters;

public sealed class PolarisGCodeAdapter(
    IOptions<MiddlewareOptions> options,
    ILogger<PolarisGCodeAdapter> logger) : IPolarisGCodeAdapter
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

        logger.LogInformation(
            "Uploading local G-code file {LocalFilePath} to Polaris {ServerIpAddress} as remote file {RemoteFileName} in directory {RemoteDirectory}.",
            localFilePath,
            polarisOptions.IpAddress,
            remoteFileName,
            polarisOptions.GCodeDirectory);

        lock (syncRoot)
        {
            connectedFileSystem.RemoteDirectory = polarisOptions.GCodeDirectory;
            connectedFileSystem.RemoteFile = remoteFileName;

            ExecuteWithPolarisSynchronizationContext(() => connectedFileSystem.UploadFile(localFilePath));
        }

        logger.LogInformation(
            "Completed Polaris upload for remote file {RemoteFileName}.",
            remoteFileName);

        return Task.CompletedTask;
    }

    public Task RunRemoteGCodeAsync(string remoteFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectedScript = GetConnectedScript(options.Value.Polaris.IpAddress);

        logger.LogInformation(
            "Starting remote Polaris G-code {RemoteFileName} on server {ServerIpAddress}.",
            remoteFileName,
            options.Value.Polaris.IpAddress);

        lock (syncRoot)
        {
            GCI.RunGcode(connectedScript, remoteFileName);
        }

        logger.LogInformation(
            "Run command submitted for remote Polaris G-code {RemoteFileName}.",
            remoteFileName);

        return Task.CompletedTask;
    }

    public Task<int> EnableAxisAsync(string axisName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(axisName))
        {
            throw new ArgumentException("Axis name is required.", nameof(axisName));
        }

        var polarisOptions = options.Value.Polaris;
        var connectedScript = GetConnectedScript(polarisOptions.IpAddress);
        int axisNumber;

        logger.LogInformation(
            "Resolving Polaris axis number for axis {AxisName} on server {ServerIpAddress}.",
            axisName,
            polarisOptions.IpAddress);

        lock (syncRoot)
        {
            axisNumber = PSU.GetAxisNumberFromDeviceName(connectedScript, axisName);

            logger.LogInformation(
                "Enabling Polaris axis {AxisName} resolved as axis number {AxisNumber}.",
                axisName,
                axisNumber);

            MCI.EnableAxis(connectedScript, axisNumber);
        }

        logger.LogInformation(
            "Enable command submitted for Polaris axis {AxisName} with axis number {AxisNumber}.",
            axisName,
            axisNumber);

        return Task.FromResult(axisNumber);
    }

    public Task<PolarisGCodeStatusResult> ReadGCodeStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var polarisOptions = options.Value.Polaris;
        var connectedScript = GetConnectedScript(polarisOptions.IpAddress);

        int stateCode;
        string state;
        string? error = null;

        lock (syncRoot)
        {
            stateCode = GCI.GetMode(connectedScript);
            state = MapGCodeState(stateCode);

            if (GCI.InError(connectedScript) || stateCode == 6)
            {
                try
                {
                    error = GCI.GetError(connectedScript).ToString();
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to read Polaris G-code error after aborted state.");
                    error = "Unable to read G-code error.";
                }
            }
        }

        logger.LogInformation(
            "Read Polaris G-code status {State} ({StateCode}) from server {ServerIpAddress}.",
            state,
            stateCode,
            polarisOptions.IpAddress);

        return Task.FromResult(new PolarisGCodeStatusResult(
            ServerIpAddress: polarisOptions.IpAddress,
            StateCode: stateCode,
            State: state,
            Error: error,
            ReadAtUtc: DateTimeOffset.UtcNow));
    }

    public Task<PolarisSystemInfo> ReadSystemInfoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var polarisOptions = options.Value.Polaris;
        var connectedScript = GetConnectedScript(polarisOptions.IpAddress);

        logger.LogInformation(
            "Reading Polaris system info from server {ServerIpAddress}.",
            polarisOptions.IpAddress);

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
                logger.LogInformation("Connecting Polaris script interface to {ServerIpAddress}.", ipAddress);
                ExecuteWithPolarisSynchronizationContext(() =>
                {
                    script.Connect(ipAddress);
                    script.SetUiContext(SynchronizationContext.Current!);
                });
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
                logger.LogInformation("Connecting Polaris file system interface to {ServerIpAddress}.", ipAddress);
                fileSystem.Connect(ipAddress);
                connectedFileSystemIpAddress = ipAddress;
            }

            return fileSystem;
        }
    }

    private static string MapGCodeState(int stateCode)
    {
        return stateCode switch
        {
            0 => "Stopped",
            1 => "Ready",
            2 => "Running",
            3 => "Paused",
            4 => "StepMode",
            5 => "SteppingPaused",
            6 => "Aborted",
            7 => "Pausing",
            8 => "RunningMDI",
            _ => $"Unknown({stateCode})"
        };
    }

    private static void ExecuteWithPolarisSynchronizationContext(Action action)
    {
        var previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new PolarisTaskSchedulerSynchronizationContext());
            action();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("SynchronizationContext", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Polaris upload requires a compatible synchronization context. The middleware injected a custom context, but the Polaris library still rejected the upload. Prefer running the upload from the Polaris desktop demo or validate axis/control commands first.",
                exception);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class PolarisTaskSchedulerSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            ThreadPool.QueueUserWorkItem(static callbackState =>
            {
                var workItem = ((SendOrPostCallback Callback, object? State))callbackState!;
                workItem.Callback(workItem.State);
            }, (d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }

        public override SynchronizationContext CreateCopy()
        {
            return new PolarisTaskSchedulerSynchronizationContext();
        }
    }
}

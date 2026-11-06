namespace Middleware.Api.LocalServices;

public sealed class LocalServiceStartup(LocalServiceManager manager) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows() &&
            !string.Equals(Environment.GetEnvironmentVariable("LOCAL_SERVICE_MANAGER_ENABLED"), "false", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("LOCAL_SERVICE_AUTOSTART"), "false", StringComparison.OrdinalIgnoreCase))
            manager.Request("start", "all");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

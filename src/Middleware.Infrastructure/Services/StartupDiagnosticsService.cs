using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Middleware.Infrastructure.Services;

public sealed class StartupDiagnosticsService(
    ILogger<StartupDiagnosticsService> logger,
    MiddlewareDiagnosticsService diagnosticsService) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("=== WISE Middleware startup diagnostics ===");
        var snapshot = await diagnosticsService.GetSnapshotAsync(cancellationToken);
        foreach (var component in snapshot.Components)
        {
            if (component.Status == "healthy")
            {
                logger.LogInformation("{Component} healthy: {Summary}", component.Component, component.Summary);
            }
            else if (component.Status == "skipped")
            {
                logger.LogInformation("{Component} skipped: {Summary}", component.Component, component.Summary);
            }
            else
            {
                logger.LogError("{Component} {Status}: {Summary}", component.Component, component.Status, component.Summary);
            }
        }

        logger.LogInformation("=== Startup diagnostics completed ===");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

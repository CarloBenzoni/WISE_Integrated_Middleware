using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProcessCoordinator.Services;

public sealed class WorkflowCoordinatorWorker(
    ILogger<WorkflowCoordinatorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("ProcessCoordinator is intentionally idle in this phase.");

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

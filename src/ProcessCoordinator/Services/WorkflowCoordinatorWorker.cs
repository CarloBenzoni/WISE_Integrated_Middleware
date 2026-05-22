using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;

namespace ProcessCoordinator.Services;

public sealed class WorkflowCoordinatorWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MiddlewareOptions> options,
    ILogger<WorkflowCoordinatorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(500, options.Value.ProcessCoordinator.IdleDelayMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<ProductionWorkflowCoordinator>();
                var executedJob = await coordinator.TryExecuteNextJobAsync(stoppingToken);

                if (!executedJob)
                {
                    await Task.Delay(idleDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "ProcessCoordinator loop failed.");
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }
}

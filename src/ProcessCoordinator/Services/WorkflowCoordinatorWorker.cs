using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;
using Middleware.Infrastructure.Services;

namespace ProcessCoordinator.Services;

public sealed class WorkflowCoordinatorWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MiddlewareOptions> options,
    ILogger<WorkflowCoordinatorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(500, options.Value.ProcessCoordinator.IdleDelayMs));
        string? lastFailure = null;
        var nextReport = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<ProductionWorkflowCoordinator>();
                var executedJob = await coordinator.TryExecuteNextJobAsync(stoppingToken);
                if (lastFailure is not null) logger.LogInformation("Controllo della coda di produzione ripristinato.");
                lastFailure = null;

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
                var message = RuntimeDiagnosticMessages.ForException(exception);
                if (message != lastFailure || DateTimeOffset.UtcNow >= nextReport)
                {
                    logger.LogWarning("Controllo coda produzione non riuscito: {Reason} Nuovi tentativi automatici; riepilogo al massimo ogni minuto per lo stesso errore.", message);
                    nextReport = DateTimeOffset.UtcNow.AddMinutes(1);
                }
                lastFailure = message;
                logger.LogDebug(exception, "Dettaglio errore coordinatore produzione.");
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }
}

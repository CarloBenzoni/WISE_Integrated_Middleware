using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Middleware.Application.Services;
using Middleware.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Services;

namespace Middleware.Worker.Services;

public sealed class OpcUaIngestionWorker(
    ILogger<OpcUaIngestionWorker> logger,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<MiddlewareOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = TimeSpan.FromMilliseconds(Math.Max(options.Value.Worker.PollingIntervalMs, 100));
        string? lastFailure = null;
        var nextReport = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var ingestionService = scope.ServiceProvider.GetRequiredService<MiddlewareIngestionService>();

                var sample = await ingestionService.IngestAsync(stoppingToken);
                if (lastFailure is not null) logger.LogInformation("Acquisizione PLC ripristinata: lettura e salvataggio riusciti.");
                lastFailure = null;
                logger.LogInformation(
                    "Sample persisted for PLC {PlcId}: {VariableCount} variable(s), SourceTimestampUtc={SourceTimestampUtc}.",
                    sample.PlcId,
                    sample.Payload.Count,
                    sample.SourceTimestampUtc);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var message = RuntimeDiagnosticMessages.ForException(ex);
                if (message != lastFailure || DateTimeOffset.UtcNow >= nextReport)
                {
                    logger.LogWarning("Acquisizione PLC non disponibile: {Reason} Nuovi tentativi automatici; riepilogo al massimo ogni minuto per lo stesso errore.", message);
                    nextReport = DateTimeOffset.UtcNow.AddMinutes(1);
                }
                lastFailure = message;
                logger.LogDebug(ex, "Dettaglio errore acquisizione PLC.");
            }

            await Task.Delay(pollingInterval, stoppingToken);
        }
    }
}

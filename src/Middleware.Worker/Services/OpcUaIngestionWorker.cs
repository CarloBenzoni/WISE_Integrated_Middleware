using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Middleware.Application.Services;
using Middleware.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Middleware.Worker.Services;

public sealed class OpcUaIngestionWorker(
    ILogger<OpcUaIngestionWorker> logger,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<MiddlewareOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = TimeSpan.FromMilliseconds(Math.Max(options.Value.Worker.PollingIntervalMs, 100));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var ingestionService = scope.ServiceProvider.GetRequiredService<MiddlewareIngestionService>();

                var sample = await ingestionService.IngestAsync(stoppingToken);
                logger.LogInformation(
                    "Sample persisted for PLC {PlcId}: AxisPosition={AxisPosition}, MachineStatus={MachineStatus}, AlarmCode={AlarmCode}, SourceTimestampUtc={SourceTimestampUtc}.",
                    sample.PlcId,
                    sample.AxisPosition,
                    sample.MachineStatus,
                    sample.AlarmCode,
                    sample.SourceTimestampUtc);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Middleware ingestion cycle failed.");
            }

            await Task.Delay(pollingInterval, stoppingToken);
        }
    }
}

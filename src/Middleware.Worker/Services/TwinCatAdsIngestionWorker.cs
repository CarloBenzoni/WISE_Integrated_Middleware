using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Services;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using System.Diagnostics;

namespace Middleware.Worker.Services;

public sealed class TwinCatAdsIngestionWorker(
    ILogger<TwinCatAdsIngestionWorker> logger,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<TwinCatTelemetryOptions> options,
    TwinCatTelemetryControl control) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.Groups.Count == 0)
        {
            logger.LogWarning("TwinCAT ADS telemetry is enabled, but no polling groups are configured.");
            return;
        }

        logger.LogInformation("TwinCAT ADS telemetry worker is ready for {SourceId} with {GroupCount} polling group(s). Collection is {State}.",
            settings.SourceId, settings.Groups.Count, control.IsEnabled ? "enabled" : "disabled");

        await Task.WhenAll(settings.Groups.Select(group => PollGroupAsync(settings, group, stoppingToken)));
    }

    private async Task PollGroupAsync(
        TwinCatTelemetryOptions settings,
        TwinCatPollingGroupOptions group,
        CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(group.Name) || group.Variables.Count == 0)
        {
            logger.LogError("TwinCAT polling group is missing a name or has no variables; it will be skipped.");
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(group.PollingIntervalMs, 100));
        var variables = group.Variables.Select(variable => variable.ToDomain()).ToArray();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!control.IsEnabled)
            {
                await control.WaitUntilEnabledAsync(stoppingToken);
                continue;
            }

            var cycle = Stopwatch.StartNew();
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var ingestionService = scope.ServiceProvider.GetRequiredService<TwinCatTelemetryIngestionService>();
                var sample = await ingestionService.IngestGroupAsync(
                    settings.SourceId,
                    group.Name,
                    variables,
                    stoppingToken);

                logger.LogInformation(
                    "TwinCAT group {GroupId} persisted: {ValueCount} value(s), {ErrorCount} read error(s).",
                    group.Name,
                    sample.Payload.Count,
                    sample.ReadErrors.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "TwinCAT telemetry cycle failed for group {GroupId}.", group.Name);
            }

            var delay = interval - cycle.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await control.WaitForChangeOrTimeoutAsync(delay, stoppingToken);
            }
            else
            {
                logger.LogWarning(
                    "TwinCAT group {GroupId} took {ElapsedMs} ms, longer than its configured {IntervalMs} ms interval.",
                    group.Name,
                    cycle.ElapsedMilliseconds,
                    interval.TotalMilliseconds);
            }
        }
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Worker.Services;

public sealed class TwinCatTelemetryEnableMonitorWorker(
    ILogger<TwinCatTelemetryEnableMonitorWorker> logger,
    ITwinCatTelemetryAdapter adapter,
    TwinCatTelemetryControl control,
    IOptions<TwinCatTelemetryOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.EnableMode.Equals("MiddlewareCommand", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!settings.EnableMode.Equals("PlcSymbol", StringComparison.OrdinalIgnoreCase))
        {
            control.SetEnabled(false);
            logger.LogError("Unsupported TwinCAT EnableMode '{EnableMode}'; collection stays disabled.", settings.EnableMode);
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.EnableSymbol))
        {
            control.SetEnabled(false);
            logger.LogError("TwinCAT enable mode is PlcSymbol, but EnableSymbol is empty; collection stays disabled.");
            return;
        }

        var interval = TimeSpan.FromMilliseconds(Math.Max(settings.EnablePollingIntervalMs, 250));
        logger.LogInformation(
            "TwinCAT collection enable is controlled by PLC Boolean {EnableSymbol}, checked every {IntervalMs} ms.",
            settings.EnableSymbol,
            interval.TotalMilliseconds);

        var readFailed = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var enabled = await adapter.ReadBooleanAsync(settings.EnableSymbol, stoppingToken);
                if (readFailed)
                {
                    logger.LogInformation("TwinCAT enable symbol {EnableSymbol} is readable again.", settings.EnableSymbol);
                    readFailed = false;
                }
                if (control.SetEnabled(enabled))
                {
                    logger.LogInformation("TwinCAT telemetry collection {State} by PLC symbol {EnableSymbol}.",
                        enabled ? "enabled" : "disabled", settings.EnableSymbol);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Fail closed: if the PLC enable signal cannot be read, stop collection.
                var stateChanged = control.SetEnabled(false);
                if (!readFailed || stateChanged)
                {
                    logger.LogWarning(exception,
                        "Could not read TwinCAT enable symbol {EnableSymbol}; collection {State}.",
                        settings.EnableSymbol,
                        stateChanged ? "disabled" : "remains disabled");
                }
                readFailed = true;
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}

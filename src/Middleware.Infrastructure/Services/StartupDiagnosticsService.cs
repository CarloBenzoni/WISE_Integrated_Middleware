using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Middleware.Infrastructure.Services;

public sealed class StartupDiagnosticsService(
    ILogger<StartupDiagnosticsService> logger,
    MiddlewareDiagnosticsService diagnosticsService,
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("[{Runtime}] Inizializzazione e diagnosi in corso...", environment.ApplicationName);
        var snapshot = await diagnosticsService.GetSnapshotAsync(cancellationToken);
        // Report completion only when all hosted services have started.
        lifetime.ApplicationStarted.Register(() =>
        {
            var summary = new StringBuilder();
            summary.AppendLine($"=== {environment.ApplicationName}: INIZIALIZZAZIONE E DIAGNOSI TERMINATE ===");
            foreach (var component in snapshot.Components)
            {
                var state = component.Status == "skipped" ? "DISABILITATO" : component.IsHealthy ? "OK" : "NON DISPONIBILE";
                var message = component.IsHealthy || component.Status == "skipped"
                    ? component.Summary
                    : RuntimeDiagnosticMessages.ForComponent(component.Component, component.Summary);
                summary.AppendLine($"[{state}] {component.Component}: {message}");
                logger.LogDebug("Diagnosi completa {Component}: {Summary}", component.Component, component.Summary);
            }
            summary.AppendLine(environment.ApplicationName switch
            {
                "Middleware.Api" => "API avviata: richieste WebApp abilitate. Documenti e produzione richiedono i rispettivi servizi disponibili.",
                "Middleware.Worker" => "Routine acquisizione PLC avviata. Acquisisce dati quando PLC e MongoDB sono disponibili; altrimenti ritenta.",
                "ProcessCoordinator" => "Routine gestione coda avviata. I job richiedono MongoDB e le macchine previste dal ciclo di produzione.",
                _ => "Runtime avviato."
            });
            var missing = snapshot.Components.Where(c => !c.IsHealthy && c.Status != "skipped").Select(c => c.Component).ToArray();
            if (missing.Length == 0)
                summary.Append("ESITO: tutti i controlli abilitati sono riusciti.");
            else
                summary.Append($"ESITO: avvio completato con limitazioni. Da verificare: {string.Join(", ", missing)}. Le funzioni che ne dipendono non sono pronte.");
            logger.LogInformation("{StartupSummary}", summary.ToString());
        });
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

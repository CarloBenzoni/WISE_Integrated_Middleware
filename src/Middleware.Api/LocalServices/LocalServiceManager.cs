namespace Middleware.Api.LocalServices;

public sealed record LocalServiceOperation(
    string Action, string Target, string State, DateTimeOffset StartedAt,
    string[] Steps, string? Current = null, string? Error = null, DateTimeOffset? FinishedAt = null);

public sealed record LocalServiceSnapshot(
    bool Installed, DateTimeOffset? CheckedAt, LocalServiceState[] Services,
    LocalServiceOperation? Operation, string? Error = null, string? Message = null);

public sealed class LocalServiceManager(
    WindowsServiceTasks tasks,
    IHostApplicationLifetime lifetime,
    ILogger<LocalServiceManager> logger)
{
    private static readonly string[] Order = ["mongodb", "elasticsearch", "hdfs", "monstache"];
    private static readonly Dictionary<string, string> Names = new()
    {
        ["mongodb"] = "MongoDB", ["elasticsearch"] = "Elasticsearch",
        ["hdfs"] = "Hadoop HDFS", ["monstache"] = "Monstache"
    };
    private readonly SemaphoreSlim statusGate = new(1, 1);
    private readonly object gate = new();
    private Task? active;
    private TaskStatus status = new(false, null, []);
    private LocalServiceOperation? operation;
    private string? statusError;

    public LocalServiceSnapshot Snapshot()
    {
        lock (gate)
        {
            return new LocalServiceSnapshot(status.Installed, status.CheckedAt, CompleteStates(status.Services),
                operation, statusError ?? status.Error, status.Message);
        }
    }

    public async Task<LocalServiceSnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        await statusGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var next = await tasks.StatusAsync(cancellationToken);
                lock (gate) { status = next; statusError = null; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (gate) { statusError = ex.Message; status = status with { Installed = false }; }
            }
            return Snapshot();
        }
        finally { statusGate.Release(); }
    }

    public LocalServiceOperation Request(string action, string target)
    {
        if (!new[] { "start", "stop", "restart" }.Contains(action) ||
            target != "all" && !Names.ContainsKey(target))
            throw new ArgumentException("Unknown service or action.");
        lock (gate)
        {
            if (active is { IsCompleted: false })
                throw new InvalidOperationException("Another service operation is still running.");
            operation = new LocalServiceOperation(action, target, "running", DateTimeOffset.UtcNow, []);
            active = Task.Run(() => ExecuteAsync(action, target, lifetime.ApplicationStopping));
            return operation;
        }
    }

    private async Task ExecuteAsync(string action, string target, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await RefreshAsync(cancellationToken);
            if (!snapshot.Installed)
                throw new InvalidOperationException(snapshot.Error ??
                    "One-time Windows administrator setup is required. See the middleware local services guide.");

            var dependents = target is "mongodb" or "elasticsearch" ? new[] { "monstache" } : [];
            var restore = dependents.Where(id => Get(id).State is "running" or "degraded").ToArray();
            if (action != "start")
            {
                var targets = target == "all" ? Order.Reverse().ToArray() : dependents.Append(target).ToArray();
                foreach (var id in targets)
                {
                    var service = Get(id);
                    if (service.State != "stopped" &&
                        (!service.Controllable || service.State is "conflict" or "unknown"))
                        throw new InvalidOperationException($"{Names[id]}: {service.Message}");
                }
                foreach (var id in targets) await StopAsync(id, cancellationToken);
            }
            if (action != "stop")
            {
                if (target == "all")
                {
                    var failures = new HashSet<string>();
                    var failed = new HashSet<string>();
                    foreach (var id in Order)
                    {
                        try
                        {
                            if (id == "monstache" && (failed.Contains("mongodb") || failed.Contains("elasticsearch")))
                                throw new InvalidOperationException("Monstache: MongoDB or Elasticsearch did not start.");
                            await StartAsync(id, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            failed.Add(id);
                            if (failures.Add(ex.Message)) AddStep(ex.Message);
                        }
                    }
                    if (failures.Count > 0) throw new InvalidOperationException(string.Join(" | ", failures));
                }
                else
                {
                    if (target == "monstache")
                    {
                        await StartAsync("mongodb", cancellationToken);
                        await StartAsync("elasticsearch", cancellationToken);
                    }
                    await StartAsync(target, cancellationToken);
                    if (action == "restart")
                        foreach (var id in restore) await StartAsync(id, cancellationToken);
                }
            }
            await RefreshAsync(cancellationToken);
            ChangeOperation(current => current with
            {
                State = "completed", Current = null, FinishedAt = DateTimeOffset.UtcNow
            });
            logger.LogInformation("Local services {Action} {Target}: completed.", action, target);
            if (target == "all") LogSummary();
        }
        catch (Exception ex)
        {
            ChangeOperation(current => current with
            {
                State = "failed", Current = null, Error = ex.Message, FinishedAt = DateTimeOffset.UtcNow
            });
            logger.LogWarning("Local services {Action} {Target}: {Error}", action, target, ex.Message);
            if (target == "all") LogSummary();
        }
    }

    private void LogSummary()
    {
        var snapshot = Snapshot();
        logger.LogInformation("Inizializzazione e diagnosi dei servizi locali terminate.");
        foreach (var service in snapshot.Services)
            logger.LogInformation("{Service}: {Result} - {Message}", Names[service.Id],
                service.Ready ? "PRONTO" : "NON PRONTO", service.Message);
    }

    private async Task StartAsync(string id, CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        var service = Get(id);
        if (service.Ready) { AddStep($"{Names[id]}: already ready; no new process started."); return; }
        if (service.State != "stopped")
            throw new InvalidOperationException($"{Names[id]}: {service.Message}");
        ChangeOperation(current => current with { Current = $"Starting {Names[id]}" });
        await tasks.RunAsync(id, "start", cancellationToken);
        await WaitForAsync(id, state => state.Ready, cancellationToken);
        AddStep($"{Names[id]}: started.");
    }

    private async Task StopAsync(string id, CancellationToken cancellationToken)
    {
        await RefreshAsync(cancellationToken);
        var service = Get(id);
        if (service.State == "stopped") return;
        ChangeOperation(current => current with { Current = $"Stopping {Names[id]}" });
        await tasks.RunAsync(id, "stop", cancellationToken);
        await WaitForAsync(id, state => state.State == "stopped", cancellationToken);
        AddStep($"{Names[id]}: stopped.");
    }

    private async Task WaitForAsync(string id, Func<LocalServiceState, bool> ready, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        do
        {
            await RefreshAsync(cancellationToken);
            var service = Get(id);
            if (ready(service)) return;
            if (service.State == "conflict") throw new InvalidOperationException($"{Names[id]}: {service.Message}");
            await Task.Delay(1500, cancellationToken);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new TimeoutException($"{Names[id]} did not reach the expected state. Check its status and local logs.");
    }

    private LocalServiceState Get(string id) =>
        Snapshot().Services.First(service => service.Id == id);

    private void AddStep(string step) =>
        ChangeOperation(current => current with { Steps = [.. current.Steps, step] });

    private void ChangeOperation(Func<LocalServiceOperation, LocalServiceOperation> change)
    {
        lock (gate) if (operation is not null) operation = change(operation);
    }

    private static LocalServiceState[] CompleteStates(LocalServiceState[]? states) =>
        Order.Select(id => states?.FirstOrDefault(state => state.Id == id) ??
            new LocalServiceState(id, "unknown", false, false, "Waiting for a verified status.")).ToArray();
}

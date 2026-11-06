using System.Diagnostics;
using System.Text.Json;

namespace Middleware.Api.LocalServices;

public sealed record LocalServiceState(
    string Id,
    string State,
    bool Ready,
    bool Controllable,
    string Message,
    int[]? Pids = null);

public sealed record TaskStatus(
    bool Installed,
    DateTimeOffset? CheckedAt,
    LocalServiceState[]? Services,
    string? Message = null,
    string? Error = null);

public sealed class WindowsServiceTasks(IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string scriptPath = ResolveScriptPath(environment.ContentRootPath);

    private static string ResolveScriptPath(string contentRoot)
    {
        foreach (var root in new[] { contentRoot, Path.Combine(contentRoot, "..", "..") })
        {
            var candidate = Path.GetFullPath(Path.Combine(root, "scripts", "local-services", "Invoke-ServiceTask.ps1"));
            if (File.Exists(candidate)) return candidate;
        }
        return Path.GetFullPath(Path.Combine(contentRoot, "scripts", "local-services", "Invoke-ServiceTask.ps1"));
    }

    public async Task<TaskStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var json = await InvokeAsync("status", "all", TimeSpan.FromSeconds(45), cancellationToken);
        return JsonSerializer.Deserialize<TaskStatus>(json, JsonOptions)
            ?? throw new InvalidOperationException("Service status returned no data.");
    }

    public async Task RunAsync(string service, string action, CancellationToken cancellationToken)
    {
        var json = await InvokeAsync(action, service, TimeSpan.FromSeconds(180), cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException(error.GetString() ?? "Service task failed.");
    }

    private async Task<string> InvokeAsync(
        string action, string service, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException("Middleware service task client is missing.", scriptPath);

        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                     "-File", scriptPath, "-Action", action, "-Service", service })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not run the Windows service task client.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var result = (await output).TrimStart('\uFEFF').Trim();
        _ = await errors; // Task client returns controlled JSON; stderr is never sent to the browser.
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidOperationException("The Windows service task returned no result.");
        return result;
    }
}

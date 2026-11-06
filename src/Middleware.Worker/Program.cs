using Middleware.Application.DependencyInjection;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Infrastructure.DependencyInjection;
using Middleware.Infrastructure.Configuration;
using Middleware.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var twinCatReadCommand = TwinCatReadCommand.TryParse(args);
var hostArguments = twinCatReadCommand is null ? args : Array.Empty<string>();
var builder = Host.CreateApplicationBuilder(hostArguments);
var sharedAppSettingsPath = ResolveSharedAppSettingsPath(builder.Environment.ContentRootPath);

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(sharedAppSettingsPath, optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(Path.GetDirectoryName(sharedAppSettingsPath)!, "twincat-telemetry.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(hostArguments);

builder.Services
    .AddMiddlewareApplication()
    .AddMiddlewareInfrastructure(builder.Configuration)
    .AddTwinCatTelemetryApplication()
    .AddTwinCatTelemetryInfrastructure(builder.Configuration);

builder.Services.AddSingleton<TwinCatTelemetryControl>(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<TwinCatTelemetryOptions>>().Value;
    var initialEnabled = settings.EnableMode.Equals("MiddlewareCommand", StringComparison.OrdinalIgnoreCase)
        && settings.Enabled;
    return new TwinCatTelemetryControl(initialEnabled);
});

builder.Services.AddHostedService<OpcUaIngestionWorker>();
builder.Services.AddHostedService<TwinCatTelemetryEnableMonitorWorker>();
builder.Services.AddHostedService<TwinCatAdsIngestionWorker>();
builder.Services.AddHostedService<ConsoleCommandWorker>();

var host = builder.Build();

if (twinCatReadCommand is not null)
{
    try
    {
        await RunTwinCatReadCommandAsync(host.Services, twinCatReadCommand);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"TwinCAT ADS read failed: {exception.Message}");
        Environment.ExitCode = 1;
    }
    finally
    {
        host.Dispose();
    }

    return;
}

host.Run();

static async Task RunTwinCatReadCommandAsync(IServiceProvider services, TwinCatReadCommand command)
{
    var settings = services.GetRequiredService<IOptions<TwinCatTelemetryOptions>>().Value;
    var group = settings.Groups.FirstOrDefault(
        item => item.Name.Equals(command.GroupName, StringComparison.OrdinalIgnoreCase));

    if (group is null)
    {
        var knownGroups = string.Join(", ", settings.Groups.Select(item => item.Name));
        throw new InvalidOperationException(
            $"Unknown group '{command.GroupName}'. Configured groups: {knownGroups}.");
    }

    var selectedVariables = group.Variables.AsEnumerable();
    if (command.Keys.Count > 0)
    {
        var requestedKeys = new HashSet<string>(command.Keys, StringComparer.OrdinalIgnoreCase);
        var foundKeys = group.Variables
            .Where(variable => requestedKeys.Contains(variable.Key))
            .Select(variable => variable.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownKeys = requestedKeys.Except(foundKeys, StringComparer.OrdinalIgnoreCase).ToArray();

        if (unknownKeys.Length > 0)
        {
            throw new InvalidOperationException(
                $"Unknown key(s) in group '{group.Name}': {string.Join(", ", unknownKeys)}.");
        }

        selectedVariables = group.Variables.Where(variable => requestedKeys.Contains(variable.Key));
    }

    var adapter = services.GetRequiredService<ITwinCatTelemetryAdapter>();
    var sample = await adapter.ReadGroupAsync(
        settings.SourceId,
        group.Name,
        selectedVariables.Select(variable => variable.ToDomain()).ToArray(),
        CancellationToken.None);

    Console.WriteLine($"TwinCAT ADS group '{group.Name}' at {sample.ObservedAtUtc:O}");
    foreach (var item in sample.Payload.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"{item.Key} = {item.Value.RawValue} [{item.Value.ValueType}]");
    }

    foreach (var item in sample.ReadErrors.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"ERROR {item.Key}: {item.Value}");
    }

    if (sample.Payload.Count == 0 && sample.ReadErrors.Count == 0)
    {
        Console.WriteLine("No variables were selected.");
    }

    Console.WriteLine("Read-only manual check: no sample was saved to MongoDB.");
}

static string ResolveSharedAppSettingsPath(string contentRootPath)
{
    var candidatePaths = new[]
    {
        Path.Combine(contentRootPath, "appsettings.json"),
        Path.Combine(contentRootPath, "..", "..", "appsettings.json"),
    };

    foreach (var candidatePath in candidatePaths)
    {
        var fullPath = Path.GetFullPath(candidatePath);
        if (File.Exists(fullPath))
        {
            return fullPath;
        }
    }

    throw new FileNotFoundException(
        "Shared configuration file 'appsettings.json' was not found.",
        Path.GetFullPath(candidatePaths[0]));
}

using Middleware.Application.DependencyInjection;
using Middleware.Infrastructure.DependencyInjection;
using Middleware.Api.LocalServices;

var builder = WebApplication.CreateBuilder(args);
var sharedAppSettingsPath = ResolveSharedAppSettingsPath(builder.Environment.ContentRootPath);

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(sharedAppSettingsPath, optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(Path.GetDirectoryName(sharedAppSettingsPath)!, "twincat-telemetry.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services
    .AddMiddlewareApplication()
    .AddMiddlewareInfrastructure(builder.Configuration)
    .AddTwinCatTelemetryInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddSingleton<WindowsServiceTasks>();
builder.Services.AddSingleton<LocalServiceManager>();
builder.Services.AddHostedService<LocalServiceStartup>();

var app = builder.Build();

app.UseAuthorization();
app.MapControllers();

app.Run();

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

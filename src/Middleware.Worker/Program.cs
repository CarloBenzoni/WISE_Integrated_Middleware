using Middleware.Application.DependencyInjection;
using Middleware.Infrastructure.DependencyInjection;
using Middleware.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var sharedAppSettingsPath = ResolveSharedAppSettingsPath(builder.Environment.ContentRootPath);

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(sharedAppSettingsPath, optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services
    .AddMiddlewareApplication()
    .AddMiddlewareInfrastructure(builder.Configuration);

builder.Services.AddHostedService<OpcUaIngestionWorker>();
builder.Services.AddHostedService<ConsoleCommandWorker>();

var host = builder.Build();
host.Run();

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

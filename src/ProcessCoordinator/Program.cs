using Middleware.Application.DependencyInjection;
using Middleware.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProcessCoordinator.Services;

var builder = Host.CreateApplicationBuilder(args);
var sharedAppSettingsPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "appsettings.json"));

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(sharedAppSettingsPath, optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services
    .AddMiddlewareApplication()
    .AddMiddlewareInfrastructure(builder.Configuration);

builder.Services.AddScoped<ProductionWorkflowCoordinator>();
builder.Services.AddHostedService<WorkflowCoordinatorWorker>();

var host = builder.Build();
host.Run();

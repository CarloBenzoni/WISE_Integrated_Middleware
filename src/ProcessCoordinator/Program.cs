using Middleware.Application.DependencyInjection;
using Middleware.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProcessCoordinator.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMiddlewareApplication()
    .AddMiddlewareInfrastructure(builder.Configuration);

builder.Services.AddHostedService<WorkflowCoordinatorWorker>();

var host = builder.Build();
host.Run();

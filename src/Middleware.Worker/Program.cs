using Middleware.Application.DependencyInjection;
using Middleware.Infrastructure.DependencyInjection;
using Middleware.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddMiddlewareApplication()
    .AddMiddlewareInfrastructure(builder.Configuration);

builder.Services.AddHostedService<OpcUaIngestionWorker>();
builder.Services.AddHostedService<ConsoleCommandWorker>();

var host = builder.Build();
host.Run();

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Infrastructure.Adapters;
using Middleware.Infrastructure.Configuration;
using Middleware.Infrastructure.Repositories;
using Middleware.Infrastructure.Services;

namespace Middleware.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMiddlewareInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MiddlewareOptions>(configuration.GetSection("Middleware"));

        services.AddHttpClient("webhdfs");
        services.AddSingleton<IOpcUaAdapter, OpcUaAdapter>();
        services.AddSingleton<IFileStorageAdapter, WebHdfsFileStorageAdapter>();
        services.AddSingleton<IPolarisGCodeAdapter, PolarisGCodeAdapter>();
        services.AddSingleton<IPlcTelemetrySampleRepository, MongoPlcTelemetrySampleRepository>();
        services.AddSingleton<IFileArtifactMetadataRepository, MongoFileArtifactMetadataRepository>();
        services.AddSingleton<MiddlewareDiagnosticsService>();
        services.AddHostedService<StartupDiagnosticsService>();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace Middleware.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMiddlewareApplication(this IServiceCollection services)
    {
        services.AddScoped<Services.MiddlewareIngestionService>();
        services.AddScoped<Services.FileUploadService>();
        services.AddScoped<Services.PolarisGCodeService>();
        services.AddScoped<Services.PolarisInfoService>();

        return services;
    }
}

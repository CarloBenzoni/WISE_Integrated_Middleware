using Middleware.Application.DependencyInjection;
using Middleware.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
var sharedAppSettingsPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "appsettings.json"));

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(sharedAppSettingsPath, optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services
    .AddMiddlewareApplication()
    .AddMiddlewareInfrastructure(builder.Configuration);

builder.Services.AddControllers();

var app = builder.Build();

app.UseAuthorization();
app.MapControllers();

app.Run();

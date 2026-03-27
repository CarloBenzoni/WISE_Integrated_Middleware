using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Services;
using Middleware.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Microsoft.Extensions.DependencyInjection;

namespace Middleware.Infrastructure.Services;

public sealed class StartupDiagnosticsService(
    ILogger<StartupDiagnosticsService> logger,
    IOptions<MiddlewareOptions> options,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("=== WISE Middleware startup diagnostics ===");
        await ProbeMongoAsync(cancellationToken);
        await ProbeWebHdfsAsync(cancellationToken);
        await ProbeDataNodeAsync(cancellationToken);
        await ProbeOpcUaAsync(cancellationToken);
        await ProbePolarisAsync(cancellationToken);
        logger.LogInformation("=== Startup diagnostics completed ===");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ProbeMongoAsync(CancellationToken cancellationToken)
    {
        var mongoOptions = options.Value.Mongo;

        try
        {
            var client = new MongoClient(mongoOptions.ConnectionString);
            var database = client.GetDatabase(mongoOptions.Database);
            await database.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: cancellationToken);

            logger.LogInformation(
                "MongoDB reachable: {ConnectionString} | Database={Database} | PLC Collection={PlcCollection} | File Collection={FileCollection}",
                mongoOptions.ConnectionString,
                mongoOptions.Database,
                mongoOptions.PlcTelemetryCollection,
                mongoOptions.FileMetadataCollection);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "MongoDB probe failed: {ConnectionString} | Database={Database}",
                mongoOptions.ConnectionString,
                mongoOptions.Database);
        }
    }

    private async Task ProbeWebHdfsAsync(CancellationToken cancellationToken)
    {
        var storageOptions = options.Value.Storage;

        try
        {
            var client = httpClientFactory.CreateClient("webhdfs");
            var baseUrl = NormalizeWebHdfsBaseUrl(storageOptions.WebHdfsBaseUrl);
            var uri = new Uri(
                $"{baseUrl}/webhdfs/v1/?op=GETHOMEDIRECTORY&user.name={Uri.EscapeDataString(storageOptions.HdfsUser)}");

            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            logger.LogInformation(
                "WebHDFS probe status: {StatusCode} | BaseUrl={BaseUrl} | HdfsFolder={HdfsFolder} | HdfsUser={HdfsUser}",
                (int)response.StatusCode,
                baseUrl,
                storageOptions.HdfsFolderPath,
                storageOptions.HdfsUser);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "WebHDFS probe failed: {BaseUrl} | HdfsFolder={HdfsFolder}",
                storageOptions.WebHdfsBaseUrl,
                storageOptions.HdfsFolderPath);
        }
    }

    private async Task ProbeOpcUaAsync(CancellationToken cancellationToken)
    {
        var opcUaOptions = options.Value.OpcUa;

        try
        {
            var endpointUri = new Uri(opcUaOptions.EndpointUrl);
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await tcpClient.ConnectAsync(endpointUri.Host, endpointUri.Port, timeoutCts.Token);

            logger.LogInformation(
                "OPC UA endpoint reachable: {EndpointUrl} | PlcId={PlcId}",
                opcUaOptions.EndpointUrl,
                opcUaOptions.PlcId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "OPC UA probe failed: {EndpointUrl} | PlcId={PlcId}",
                opcUaOptions.EndpointUrl,
                opcUaOptions.PlcId);
        }
    }

    private async Task ProbeDataNodeAsync(CancellationToken cancellationToken)
    {
        var storageOptions = options.Value.Storage;

        if (string.IsNullOrWhiteSpace(storageOptions.DataNodeWebHdfsBaseUrl))
        {
            logger.LogInformation("DataNode WebHDFS probe skipped: no DataNodeWebHdfsBaseUrl configured.");
            return;
        }

        try
        {
            var dataNodeUri = new Uri(NormalizeWebHdfsBaseUrl(storageOptions.DataNodeWebHdfsBaseUrl));
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await tcpClient.ConnectAsync(dataNodeUri.Host, dataNodeUri.Port, timeoutCts.Token);

            logger.LogInformation(
                "DataNode WebHDFS reachable: {DataNodeWebHdfsBaseUrl} | NameNodeRpcAddress={NameNodeRpcAddress}",
                storageOptions.DataNodeWebHdfsBaseUrl,
                storageOptions.NameNodeRpcAddress);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "DataNode WebHDFS probe failed: {DataNodeWebHdfsBaseUrl} | NameNodeRpcAddress={NameNodeRpcAddress}",
                storageOptions.DataNodeWebHdfsBaseUrl,
                storageOptions.NameNodeRpcAddress);
        }
    }

    private async Task ProbePolarisAsync(CancellationToken cancellationToken)
    {
        var polarisOptions = options.Value.Polaris;

        if (!polarisOptions.Enabled)
        {
            logger.LogInformation("Polaris probe skipped: integration disabled.");
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var polarisInfoService = scope.ServiceProvider.GetRequiredService<PolarisInfoService>();
            var info = await polarisInfoService.ReadSystemInfoAsync(cancellationToken);
            logger.LogInformation(
                "Polaris reachable: {IpAddress} | Kernel={Kernel} | Status={Status} | StartupStatus={StartupStatus}",
                info.ServerIpAddress,
                info.LinuxKernelVersion,
                info.PolarisCurrentStatus,
                info.PolarisStartupStatus);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Polaris probe failed: {IpAddress}",
                polarisOptions.IpAddress);
        }
    }

    private static string NormalizeWebHdfsBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Storage.WebHdfsBaseUrl is required.");
        }

        var trimmed = baseUrl.Trim().TrimEnd('/');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid WebHDFS base URL: {baseUrl}");
        }

        if (uri.Scheme.Equals("hdfs", StringComparison.OrdinalIgnoreCase))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = Uri.UriSchemeHttp
            };

            if (builder.Port <= 0)
            {
                builder.Port = 9870;
            }

            return builder.Uri.ToString().TrimEnd('/');
        }

        return trimmed;
    }
}

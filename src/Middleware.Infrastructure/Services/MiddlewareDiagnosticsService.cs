using System.Net.Http;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Middleware.Application.Services;
using Middleware.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Middleware.Infrastructure.Services;

public sealed class MiddlewareDiagnosticsService(
    IOptions<MiddlewareOptions> options,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory)
{
    public async Task<MiddlewareDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var probes = new List<ComponentProbeResult>
        {
            await ProbeMongoAsync(cancellationToken),
            await ProbeWebHdfsAsync(cancellationToken),
            await ProbeDataNodeAsync(cancellationToken),
            await ProbeOpcUaAsync(cancellationToken),
            await ProbePolarisAsync(cancellationToken)
        };

        return new MiddlewareDiagnosticsSnapshot(
            DateTimeOffset.UtcNow,
            probes.All(probe => probe.IsHealthy || probe.Status == "skipped"),
            probes);
    }

    private async Task<ComponentProbeResult> ProbeMongoAsync(CancellationToken cancellationToken)
    {
        var mongoOptions = options.Value.Mongo;

        try
        {
            var client = new MongoClient(mongoOptions.ConnectionString);
            var database = client.GetDatabase(mongoOptions.Database);
            await database.RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: cancellationToken);

            return new ComponentProbeResult(
                "MongoDB",
                true,
                "healthy",
                $"Database={mongoOptions.Database}",
                new Dictionary<string, string>
                {
                    ["connectionString"] = mongoOptions.ConnectionString,
                    ["database"] = mongoOptions.Database,
                    ["plcCollection"] = mongoOptions.PlcTelemetryCollection,
                    ["fileCollection"] = mongoOptions.FileMetadataCollection
                });
        }
        catch (Exception exception)
        {
            return BuildFailure("MongoDB", exception, new Dictionary<string, string>
            {
                ["connectionString"] = mongoOptions.ConnectionString,
                ["database"] = mongoOptions.Database
            });
        }
    }

    private async Task<ComponentProbeResult> ProbeWebHdfsAsync(CancellationToken cancellationToken)
    {
        var storageOptions = options.Value.Storage;

        try
        {
            var client = httpClientFactory.CreateClient("webhdfs");
            var baseUrl = NormalizeWebHdfsBaseUrl(storageOptions.WebHdfsBaseUrl);
            var uri = new Uri(
                $"{baseUrl}/webhdfs/v1/?op=GETHOMEDIRECTORY&user.name={Uri.EscapeDataString(storageOptions.HdfsUser)}");

            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            return new ComponentProbeResult(
                "WebHDFS NameNode",
                response.IsSuccessStatusCode,
                response.IsSuccessStatusCode ? "healthy" : "degraded",
                $"HTTP {(int)response.StatusCode}",
                new Dictionary<string, string>
                {
                    ["baseUrl"] = baseUrl,
                    ["hdfsFolder"] = storageOptions.HdfsFolderPath,
                    ["hdfsUser"] = storageOptions.HdfsUser
                });
        }
        catch (Exception exception)
        {
            return BuildFailure("WebHDFS NameNode", exception, new Dictionary<string, string>
            {
                ["baseUrl"] = storageOptions.WebHdfsBaseUrl,
                ["hdfsFolder"] = storageOptions.HdfsFolderPath
            });
        }
    }

    private async Task<ComponentProbeResult> ProbeDataNodeAsync(CancellationToken cancellationToken)
    {
        var storageOptions = options.Value.Storage;

        if (string.IsNullOrWhiteSpace(storageOptions.DataNodeWebHdfsBaseUrl))
        {
            return new ComponentProbeResult(
                "WebHDFS DataNode",
                false,
                "skipped",
                "No DataNodeWebHdfsBaseUrl configured",
                new Dictionary<string, string>());
        }

        try
        {
            var dataNodeUri = new Uri(NormalizeWebHdfsBaseUrl(storageOptions.DataNodeWebHdfsBaseUrl));
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await tcpClient.ConnectAsync(dataNodeUri.Host, dataNodeUri.Port, timeoutCts.Token);

            return new ComponentProbeResult(
                "WebHDFS DataNode",
                true,
                "healthy",
                $"TCP {dataNodeUri.Host}:{dataNodeUri.Port}",
                new Dictionary<string, string>
                {
                    ["dataNodeBaseUrl"] = storageOptions.DataNodeWebHdfsBaseUrl,
                    ["nameNodeRpcAddress"] = storageOptions.NameNodeRpcAddress
                });
        }
        catch (Exception exception)
        {
            return BuildFailure("WebHDFS DataNode", exception, new Dictionary<string, string>
            {
                ["dataNodeBaseUrl"] = storageOptions.DataNodeWebHdfsBaseUrl,
                ["nameNodeRpcAddress"] = storageOptions.NameNodeRpcAddress
            });
        }
    }

    private async Task<ComponentProbeResult> ProbeOpcUaAsync(CancellationToken cancellationToken)
    {
        var opcUaOptions = options.Value.OpcUa;

        try
        {
            var endpointUri = new Uri(opcUaOptions.EndpointUrl);
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            await tcpClient.ConnectAsync(endpointUri.Host, endpointUri.Port, timeoutCts.Token);

            return new ComponentProbeResult(
                "OPC UA",
                true,
                "healthy",
                $"TCP {endpointUri.Host}:{endpointUri.Port}",
                new Dictionary<string, string>
                {
                    ["endpointUrl"] = opcUaOptions.EndpointUrl,
                    ["plcId"] = opcUaOptions.PlcId,
                    ["discoveryRootNodeId"] = opcUaOptions.DiscoveryRootNodeId
                });
        }
        catch (Exception exception)
        {
            return BuildFailure("OPC UA", exception, new Dictionary<string, string>
            {
                ["endpointUrl"] = opcUaOptions.EndpointUrl,
                ["plcId"] = opcUaOptions.PlcId
            });
        }
    }

    private async Task<ComponentProbeResult> ProbePolarisAsync(CancellationToken cancellationToken)
    {
        var polarisOptions = options.Value.Polaris;

        if (!polarisOptions.Enabled)
        {
            return new ComponentProbeResult(
                "Polaris",
                false,
                "skipped",
                "Integration disabled",
                new Dictionary<string, string>
                {
                    ["ipAddress"] = polarisOptions.IpAddress
                });
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var polarisInfoService = scope.ServiceProvider.GetRequiredService<PolarisInfoService>();
            var info = await polarisInfoService.ReadSystemInfoAsync(cancellationToken);

            return new ComponentProbeResult(
                "Polaris",
                true,
                "healthy",
                info.PolarisCurrentStatus,
                new Dictionary<string, string>
                {
                    ["ipAddress"] = info.ServerIpAddress,
                    ["kernel"] = info.LinuxKernelVersion,
                    ["startupStatus"] = info.PolarisStartupStatus.ToString()
                });
        }
        catch (Exception exception)
        {
            return BuildFailure("Polaris", exception, new Dictionary<string, string>
            {
                ["ipAddress"] = polarisOptions.IpAddress
            });
        }
    }

    private static ComponentProbeResult BuildFailure(
        string component,
        Exception exception,
        IReadOnlyDictionary<string, string> details)
    {
        return new ComponentProbeResult(
            component,
            false,
            "unhealthy",
            exception.Message,
            details);
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

public sealed record MiddlewareDiagnosticsSnapshot(
    DateTimeOffset GeneratedAtUtc,
    bool OverallHealthy,
    IReadOnlyCollection<ComponentProbeResult> Components);

public sealed record ComponentProbeResult(
    string Component,
    bool IsHealthy,
    string Status,
    string Summary,
    IReadOnlyDictionary<string, string> Details);

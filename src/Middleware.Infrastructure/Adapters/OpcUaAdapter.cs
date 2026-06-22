using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace Middleware.Infrastructure.Adapters;

public sealed class OpcUaAdapter(
    IOptions<MiddlewareOptions> options,
    ILogger<OpcUaAdapter> logger) : IOpcUaAdapter, IAsyncDisposable
{
    private readonly OpcUaOptions opcUaOptions = options.Value.OpcUa;
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private readonly SemaphoreSlim discoveryLock = new(1, 1);
    private readonly object reconnectSync = new();
    private readonly ITelemetryContext telemetryContext = new NullTelemetryContext();
    private ApplicationConfiguration? configuration;
    private Session? session;
    private SessionReconnectHandler? reconnectHandler;
    private IReadOnlyList<ResolvedTelemetryNode>? cachedTelemetryNodes;
    private bool disposed;

    public async Task<PlcTelemetrySample> ReadCurrentSampleAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);

        var telemetryNodes = await ResolveTelemetryNodesAsync(forceRefresh: false, cancellationToken);
        var sample = await ReadSampleAsync(telemetryNodes, cancellationToken);

        if (sample is null)
        {
            telemetryNodes = await ResolveTelemetryNodesAsync(forceRefresh: true, cancellationToken);
            sample = await ReadSampleAsync(telemetryNodes, cancellationToken);
        }

        if (sample is null)
        {
            throw new InvalidOperationException(
                $"Unable to read any OPC UA variables under {opcUaOptions.DiscoveryRootNodeId} for PLC {opcUaOptions.PlcId}.");
        }

        logger.LogInformation(
            "OPC UA sample read from {PlcId}: {VariableCount} variable(s) persisted [{VariableKeys}].",
            sample.PlcId,
            sample.Payload.Count,
            string.Join(", ", sample.Payload.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)));

        return sample;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        reconnectHandler?.Dispose();
        connectionLock.Dispose();
        discoveryLock.Dispose();

        if (session is not null)
        {
            try
            {
                await session.CloseAsync(CancellationToken.None);
            }
            catch
            {
            }

            session.Dispose();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (session is { Connected: true })
        {
            return;
        }

        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (session is { Connected: true })
            {
                return;
            }

            if (configuration is null)
            {
                configuration = await BuildConfigurationAsync(cancellationToken);
            }

            var endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                configuration,
                opcUaOptions.EndpointUrl,
                opcUaOptions.UseSecurity,
                15000,
                telemetryContext,
                cancellationToken);

            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
            var userIdentity = BuildUserIdentity(opcUaOptions.User);

            session = (Session)await new DefaultSessionFactory(telemetryContext).CreateAsync(
                configuration,
                configuredEndpoint,
                false,
                false,
                opcUaOptions.SessionName,
                (uint)opcUaOptions.SessionTimeoutMs,
                userIdentity,
                null,
                cancellationToken);

            session.KeepAlive += OnKeepAlive;
            cachedTelemetryNodes = null;

            logger.LogInformation(
                "Connected to OPC UA endpoint {EndpointUrl} for PLC {PlcId}.",
                opcUaOptions.EndpointUrl,
                opcUaOptions.PlcId);
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async Task<ApplicationConfiguration> BuildConfigurationAsync(CancellationToken cancellationToken)
    {
        var pkiPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WiseIntegratedMiddleware",
            "pki");

        var appConfiguration = new ApplicationConfiguration
        {
            ApplicationName = "WiseIntegratedMiddleware",
            ApplicationUri = $"urn:{Utils.GetHostName()}:WiseIntegratedMiddleware",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = opcUaOptions.AutoAcceptUntrustedCertificates,
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "own"),
                    SubjectName = "CN=WiseIntegratedMiddleware"
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "issuer")
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "trusted")
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pkiPath, "rejected")
                }
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 15000
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = opcUaOptions.SessionTimeoutMs
            }
        };

        await appConfiguration.ValidateAsync(ApplicationType.Client);
        appConfiguration.CertificateValidator.CertificateValidation += OnCertificateValidation;

        var appInstance = new ApplicationInstance(appConfiguration, telemetryContext);
        var haveCertificate = await appInstance.CheckApplicationInstanceCertificatesAsync(false, 2048, cancellationToken);
        if (!haveCertificate)
        {
            throw new InvalidOperationException("OPC UA application certificate not available.");
        }

        return appConfiguration;
    }

    private void OnCertificateValidation(CertificateValidator _, CertificateValidationEventArgs args)
    {
        if (opcUaOptions.AutoAcceptUntrustedCertificates &&
            args.Error.StatusCode == StatusCodes.BadCertificateUntrusted)
        {
            args.Accept = true;
            logger.LogWarning("Auto-accepted untrusted OPC UA certificate {CertificateSubject}.", args.Certificate.Subject);
        }
    }

    private void OnKeepAlive(ISession currentSession, KeepAliveEventArgs args)
    {
        if (!ServiceResult.IsBad(args.Status))
        {
            return;
        }

        lock (reconnectSync)
        {
            if (reconnectHandler is not null)
            {
                return;
            }

            logger.LogWarning("OPC UA keepalive bad for PLC {PlcId}: {Status}. Starting reconnect.", opcUaOptions.PlcId, args.Status);
            reconnectHandler = new SessionReconnectHandler(telemetryContext, true, -1);
            reconnectHandler.BeginReconnect(currentSession, opcUaOptions.ReconnectPeriodMs, OnReconnectComplete);
        }
    }

    private void OnReconnectComplete(object? sender, EventArgs e)
    {
        lock (reconnectSync)
        {
            if (!ReferenceEquals(sender, reconnectHandler))
            {
                return;
            }

            if (reconnectHandler?.Session is Session reconnectedSession)
            {
                session = reconnectedSession;
                cachedTelemetryNodes = null;
                logger.LogInformation("OPC UA session reconnected for PLC {PlcId}.", opcUaOptions.PlcId);
            }

            reconnectHandler?.Dispose();
            reconnectHandler = null;
        }
    }

    private static IUserIdentity BuildUserIdentity(OpcUaUserOptions user)
    {
        if (user.Mode.Equals("username", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(user.UserName) || string.IsNullOrWhiteSpace(user.Password))
            {
                throw new InvalidOperationException("UserName and Password are required for OPC UA username mode.");
            }

            var passwordBytes = System.Text.Encoding.UTF8.GetBytes(user.Password);
            return new UserIdentity(user.UserName, passwordBytes);
        }

        return new UserIdentity(new AnonymousIdentityToken());
    }

    private async Task<IReadOnlyList<ResolvedTelemetryNode>> ResolveTelemetryNodesAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh && cachedTelemetryNodes is { Count: > 0 })
        {
            return cachedTelemetryNodes;
        }

        await discoveryLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && cachedTelemetryNodes is { Count: > 0 })
            {
                return cachedTelemetryNodes;
            }

            var discoveredNodes = await DiscoverVariableNodesAsync(
                opcUaOptions.DiscoveryRootNodeId,
                opcUaOptions.RecursiveBrowse,
                cancellationToken);

            if (discoveredNodes.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No OPC UA variables discovered under {opcUaOptions.DiscoveryRootNodeId}.");
            }

            cachedTelemetryNodes = discoveredNodes;

            logger.LogInformation(
                "Resolved {VariableCount} OPC UA variable node(s) via discovery root {DiscoveryRootNodeId}: {VariableKeys}.",
                discoveredNodes.Count,
                opcUaOptions.DiscoveryRootNodeId,
                string.Join(", ", discoveredNodes.Select(node => node.PayloadKey)));

            return cachedTelemetryNodes;
        }
        finally
        {
            discoveryLock.Release();
        }
    }

    private async Task<PlcTelemetrySample?> ReadSampleAsync(
        IReadOnlyList<ResolvedTelemetryNode> telemetryNodes,
        CancellationToken cancellationToken)
    {
        var nodesToRead = new ReadValueIdCollection(telemetryNodes.Select(node => new ReadValueId
        {
            NodeId = node.NodeId,
            AttributeId = Attributes.Value
        }));

        var response = await session!.ReadAsync(
            null,
            0,
            TimestampsToReturn.Both,
            nodesToRead,
            cancellationToken);

        var payload = new Dictionary<string, PlcTelemetryValue>(StringComparer.OrdinalIgnoreCase);
        var sourceTimestamps = new List<DateTimeOffset>();
        var staleTopologyDetected = false;

        for (var index = 0; index < telemetryNodes.Count; index++)
        {
            var node = telemetryNodes[index];
            var result = response.Results[index];

            if (StatusCode.IsBad(result.StatusCode))
            {
                if (IndicatesStaleNode(result.StatusCode))
                {
                    staleTopologyDetected = true;
                }

                logger.LogWarning(
                    "Skipping OPC UA node {PayloadKey} ({NodeId}) because read status is {StatusCode}.",
                    node.PayloadKey,
                    node.NodeId,
                    result.StatusCode);
                continue;
            }

            payload[node.PayloadKey] = ConvertValue(result.Value);

            if (result.SourceTimestamp != DateTime.MinValue)
            {
                sourceTimestamps.Add(
                    new DateTimeOffset(DateTime.SpecifyKind(result.SourceTimestamp, DateTimeKind.Utc)));
            }
        }

        if (staleTopologyDetected)
        {
            cachedTelemetryNodes = null;
            return null;
        }

        if (payload.Count == 0)
        {
            throw new InvalidOperationException(
                $"OPC UA read returned no valid variables for PLC {opcUaOptions.PlcId}.");
        }

        return new PlcTelemetrySample(
            PlcId: opcUaOptions.PlcId,
            Payload: payload,
            SourceTimestampUtc: sourceTimestamps.DefaultIfEmpty(DateTimeOffset.UtcNow).Max(),
            IngestedAtUtc: DateTimeOffset.UtcNow);
    }

    private async Task<List<ResolvedTelemetryNode>> DiscoverVariableNodesAsync(
        string rootNodeIdString,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var rootNodeId = NodeId.Parse(rootNodeIdString);
        var queue = new Queue<NodeId>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variables = new List<ResolvedTelemetryNode>();
        var usedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        queue.Enqueue(rootNodeId);
        visited.Add(rootNodeId.ToString());

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nodeId = queue.Dequeue();
            var references = await BrowseAllAsync(nodeId, cancellationToken);

            foreach (var reference in references)
            {
                var childNodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session!.NamespaceUris);
                if (childNodeId is null)
                {
                    continue;
                }

                if (reference.NodeClass == NodeClass.Variable)
                {
                    var baseKey = NormalizePayloadKey(
                        reference.BrowseName?.Name ??
                        reference.DisplayName?.Text ??
                        childNodeId.ToString());
                    var payloadKey = BuildUniquePayloadKey(baseKey, usedKeys);
                    variables.Add(new ResolvedTelemetryNode(childNodeId.ToString(), payloadKey));
                }

                if (recursive &&
                    (reference.NodeClass == NodeClass.Object || reference.NodeClass == NodeClass.Variable) &&
                    visited.Add(childNodeId.ToString()))
                {
                    queue.Enqueue(childNodeId);
                }
            }
        }

        return variables;
    }

    private async Task<IList<ReferenceDescription>> BrowseAllAsync(NodeId nodeId, CancellationToken cancellationToken)
    {
        var browseDescription = new BrowseDescription
        {
            NodeId = nodeId,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable),
            ResultMask = (uint)BrowseResultMask.All
        };

        var references = new List<ReferenceDescription>();
        var browseResponse = await session!.BrowseAsync(
            null,
            null,
            0,
            new BrowseDescriptionCollection { browseDescription },
            cancellationToken);

        if (browseResponse.Results.Count == 0)
        {
            return references;
        }

        references.AddRange(browseResponse.Results[0].References ?? []);
        var continuationPoint = browseResponse.Results[0].ContinuationPoint;

        while (continuationPoint is { Length: > 0 })
        {
            var continuationPoints = new ByteStringCollection { continuationPoint };
            var nextResponse = await session.BrowseNextAsync(
                null,
                false,
                continuationPoints,
                cancellationToken);

            if (nextResponse.Results.Count == 0)
            {
                break;
            }

            references.AddRange(nextResponse.Results[0].References ?? []);
            continuationPoint = nextResponse.Results[0].ContinuationPoint;
        }

        return references;
    }

    private static PlcTelemetryValue ConvertValue(object? value)
    {
        if (value is null)
        {
            return new PlcTelemetryValue(string.Empty, null, "null");
        }

        if (value is bool boolValue)
        {
            return new PlcTelemetryValue(
                boolValue.ToString(),
                boolValue ? 1d : 0d,
                "boolean");
        }

        if (TryConvertToDouble(value, out var numericValue))
        {
            return new PlcTelemetryValue(
                numericValue.ToString(CultureInfo.InvariantCulture),
                numericValue,
                "number");
        }

        if (value is byte[] bytes)
        {
            return new PlcTelemetryValue(Convert.ToBase64String(bytes), null, "binary");
        }

        return new PlcTelemetryValue(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            null,
            "string");
    }

    private static bool TryConvertToDouble(object value, out double numericValue)
    {
        switch (value)
        {
            case sbyte sbyteValue:
                numericValue = sbyteValue;
                return true;
            case byte byteValue:
                numericValue = byteValue;
                return true;
            case short shortValue:
                numericValue = shortValue;
                return true;
            case ushort ushortValue:
                numericValue = ushortValue;
                return true;
            case int intValue:
                numericValue = intValue;
                return true;
            case uint uintValue:
                numericValue = uintValue;
                return true;
            case long longValue:
                numericValue = longValue;
                return true;
            case ulong ulongValue:
                numericValue = ulongValue;
                return true;
            case float floatValue:
                numericValue = floatValue;
                return true;
            case double doubleValue:
                numericValue = doubleValue;
                return true;
            case decimal decimalValue:
                numericValue = (double)decimalValue;
                return true;
            default:
                numericValue = default;
                return false;
        }
    }

    private static bool IndicatesStaleNode(StatusCode statusCode)
    {
        return statusCode == StatusCodes.BadNodeIdUnknown ||
               statusCode == StatusCodes.BadNodeIdInvalid ||
               statusCode == StatusCodes.BadNoMatch;
    }

    private static string NormalizePayloadKey(string value)
    {
        var characters = value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || character == '_' || character == '-'
                ? character
                : '_')
            .ToArray();

        var normalized = new string(characters).Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "variable";
        }

        return normalized.StartsWith('$') ? $"var_{normalized.TrimStart('$')}" : normalized;
    }

    private static string BuildUniquePayloadKey(string baseKey, IDictionary<string, int> usedKeys)
    {
        if (!usedKeys.TryGetValue(baseKey, out var count))
        {
            usedKeys[baseKey] = 1;
            return baseKey;
        }

        count++;
        usedKeys[baseKey] = count;
        return $"{baseKey}_{count}";
    }

    private sealed class NullTelemetryContext : ITelemetryContext
    {
        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
        public ActivitySource ActivitySource { get; } = new(nameof(OpcUaAdapter));

        public Meter CreateMeter()
        {
            return new Meter(nameof(OpcUaAdapter));
        }
    }

    private sealed record ResolvedTelemetryNode(string NodeId, string PayloadKey);
}

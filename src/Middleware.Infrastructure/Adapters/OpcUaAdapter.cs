using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using System.Globalization;

namespace Middleware.Infrastructure.Adapters;

public sealed class OpcUaAdapter(
    IOptions<MiddlewareOptions> options,
    ILogger<OpcUaAdapter> logger) : IOpcUaAdapter, IAsyncDisposable
{
    private readonly OpcUaOptions opcUaOptions = options.Value.OpcUa;
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private readonly object reconnectSync = new();
    private ApplicationConfiguration? configuration;
    private Session? session;
    private SessionReconnectHandler? reconnectHandler;
    private bool disposed;

    public async Task<PlcTelemetrySample> ReadCurrentSampleAsync(CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);

        var nodeIds = await ResolveTelemetryNodeIdsAsync(cancellationToken);

        var nodesToRead = new ReadValueIdCollection(nodeIds.Select(nodeId => new ReadValueId
        {
            NodeId = nodeId,
            AttributeId = Attributes.Value
        }));

        var response = await session!.ReadAsync(
            null,
            0,
            TimestampsToReturn.Both,
            nodesToRead,
            cancellationToken);

        ValidateReadResult(nodeIds[0], response.Results[0].StatusCode);
        ValidateReadResult(nodeIds[1], response.Results[1].StatusCode);
        ValidateReadResult(nodeIds[2], response.Results[2].StatusCode);

        var axisPosition = Convert.ToDouble(response.Results[0].Value, CultureInfo.InvariantCulture);
        var machineStatus = Convert.ToString(response.Results[1].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var alarmCode = Convert.ToInt32(response.Results[2].Value, CultureInfo.InvariantCulture);

        var sourceTimestamp = new[]
        {
            response.Results[0].SourceTimestamp,
            response.Results[1].SourceTimestamp,
            response.Results[2].SourceTimestamp
        }
        .Where(timestamp => timestamp != DateTime.MinValue)
        .Select(timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)))
        .DefaultIfEmpty(DateTimeOffset.UtcNow)
        .Max();

        var sample = new PlcTelemetrySample(
            PlcId: opcUaOptions.PlcId,
            AxisPosition: axisPosition,
            MachineStatus: machineStatus,
            AlarmCode: alarmCode,
            SourceTimestampUtc: sourceTimestamp,
            IngestedAtUtc: DateTimeOffset.UtcNow);

        logger.LogInformation(
            "OPC UA sample read from {PlcId}: AxisPosition={AxisPosition}, MachineStatus={MachineStatus}, AlarmCode={AlarmCode}.",
            sample.PlcId,
            sample.AxisPosition,
            sample.MachineStatus,
            sample.AlarmCode);

        return sample;
    }

    private async Task<string[]> ResolveTelemetryNodeIdsAsync(CancellationToken cancellationToken)
    {
        if (HasExplicitNodeMappings())
        {
            return
            [
                opcUaOptions.Nodes.AxisPositionNodeId,
                opcUaOptions.Nodes.MachineStatusNodeId,
                opcUaOptions.Nodes.AlarmCodeNodeId
            ];
        }

        var discoveredNodes = await DiscoverVariableNodesAsync(
            opcUaOptions.DiscoveryRootNodeId,
            opcUaOptions.RecursiveBrowse,
            cancellationToken);

        var axisPositionNodeId = FindNodeIdByName(discoveredNodes, opcUaOptions.BrowseNames.AxisPositionBrowseName);
        var machineStatusNodeId = FindNodeIdByName(discoveredNodes, opcUaOptions.BrowseNames.MachineStatusBrowseName);
        var alarmCodeNodeId = FindNodeIdByName(discoveredNodes, opcUaOptions.BrowseNames.AlarmCodeBrowseName);

        if (axisPositionNodeId is null || machineStatusNodeId is null || alarmCodeNodeId is null)
        {
            var discoveredNames = string.Join(", ", discoveredNodes.Select(node => node.DisplayName));
            throw new InvalidOperationException(
                $"Unable to resolve required OPC UA nodes under {opcUaOptions.DiscoveryRootNodeId}. Discovered nodes: {discoveredNames}.");
        }

        logger.LogInformation(
            "Resolved OPC UA telemetry nodes via discovery root {DiscoveryRootNodeId}: AxisPosition={AxisPositionNodeId}, MachineStatus={MachineStatusNodeId}, AlarmCode={AlarmCodeNodeId}.",
            opcUaOptions.DiscoveryRootNodeId,
            axisPositionNodeId,
            machineStatusNodeId,
            alarmCodeNodeId);

        return [axisPositionNodeId, machineStatusNodeId, alarmCodeNodeId];
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
                cancellationToken);

            var endpointConfiguration = EndpointConfiguration.Create(configuration);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
            var userIdentity = BuildUserIdentity(opcUaOptions.User);

            session = await Session.Create(
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

        var appInstance = new ApplicationInstance(appConfiguration);
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
            reconnectHandler = new SessionReconnectHandler(true);
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

    private static void ValidateReadResult(string nodeId, StatusCode statusCode)
    {
        if (StatusCode.IsBad(statusCode))
        {
            throw new InvalidOperationException($"OPC UA read failed for node {nodeId}: {statusCode}.");
        }
    }

    private bool HasExplicitNodeMappings()
    {
        return !string.IsNullOrWhiteSpace(opcUaOptions.Nodes.AxisPositionNodeId) &&
               !string.IsNullOrWhiteSpace(opcUaOptions.Nodes.MachineStatusNodeId) &&
               !string.IsNullOrWhiteSpace(opcUaOptions.Nodes.AlarmCodeNodeId);
    }

    private async Task<List<DiscoveredNode>> DiscoverVariableNodesAsync(
        string rootNodeIdString,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var rootNodeId = NodeId.Parse(rootNodeIdString);
        var queue = new Queue<NodeId>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var variables = new List<DiscoveredNode>();

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
                    variables.Add(new DiscoveredNode(
                        childNodeId.ToString(),
                        reference.DisplayName?.Text ?? reference.BrowseName?.Name ?? childNodeId.ToString()));
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

    private static string? FindNodeIdByName(IEnumerable<DiscoveredNode> discoveredNodes, string expectedName)
    {
        return discoveredNodes
            .FirstOrDefault(node => string.Equals(node.DisplayName, expectedName, StringComparison.OrdinalIgnoreCase))
            ?.NodeId;
    }

    private sealed record DiscoveredNode(string NodeId, string DisplayName);
}

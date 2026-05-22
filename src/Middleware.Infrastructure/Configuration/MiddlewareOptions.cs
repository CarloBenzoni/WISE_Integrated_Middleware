using Opc.Ua;

namespace Middleware.Infrastructure.Configuration;

public sealed class MiddlewareOptions
{
    public MongoOptions Mongo { get; set; } = new();
    public OpcUaOptions OpcUa { get; set; } = new();
    public StorageOptions Storage { get; set; } = new();
    public PolarisOptions Polaris { get; set; } = new();
    public WorkerOptions Worker { get; set; } = new();
    public ProcessCoordinatorOptions ProcessCoordinator { get; set; } = new();
}

public sealed class MongoOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string Database { get; set; } = "WISE";
    public string PlcTelemetryCollection { get; set; } = "integration_plc_telemetry";
    public string FileMetadataCollection { get; set; } = "integration_file_artifacts";
    public string ProductionDesignCollection { get; set; } = "production_designs";
    public string ProductionJobCollection { get; set; } = "production_jobs";
}

public sealed class WorkerOptions
{
    public int PollingIntervalMs { get; set; } = 1000;
}

public sealed class ProcessCoordinatorOptions
{
    public int IdleDelayMs { get; set; } = 3000;
    public int GCodeStatusPollingIntervalMs { get; set; } = 1000;
    public int StepCompletionTimeoutMs { get; set; } = 600000;
}

public sealed class OpcUaOptions
{
    public string PlcId { get; set; } = "S7-1200-Station_1";
    public string EndpointUrl { get; set; } = "opc.tcp://192.168.1.121:4840";
    public bool UseSecurity { get; set; }
    public MessageSecurityMode SecurityMode { get; set; } = MessageSecurityMode.None;
    public string SecurityPolicyUri { get; set; } = SecurityPolicies.None;
    public bool AutoAcceptUntrustedCertificates { get; set; } = true;
    public string SessionName { get; set; } = "WISE Integrated Middleware OPC UA Client";
    public int SessionTimeoutMs { get; set; } = 60000;
    public int ReconnectPeriodMs { get; set; } = 10000;
    public string DiscoveryRootNodeId { get; set; } = "i=85";
    public bool RecursiveBrowse { get; set; } = true;
    public OpcUaUserOptions User { get; set; } = new();
    public PlcNodeMappingOptions Nodes { get; set; } = new();
    public PlcNodeBrowseNameOptions BrowseNames { get; set; } = new();
}

public sealed class OpcUaUserOptions
{
    public string Mode { get; set; } = "Anonymous";
    public string? UserName { get; set; }
    public string? Password { get; set; }
}

public sealed class PlcNodeMappingOptions
{
    public string AxisPositionNodeId { get; set; } = string.Empty;
    public string MachineStatusNodeId { get; set; } = string.Empty;
    public string AlarmCodeNodeId { get; set; } = string.Empty;
}

public sealed class PlcNodeBrowseNameOptions
{
    public string AxisPositionBrowseName { get; set; } = "actualSpeed";
    public string MachineStatusBrowseName { get; set; } = "isActive";
    public string AlarmCodeBrowseName { get; set; } = "setpointSpeed";
}

public sealed class StorageOptions
{
    public string UploadFolderPath { get; set; } = "C:/Users/carlo/Desktop/WISE_testStorage_UploadingFolder";
    public string DownloadFolderPath { get; set; } = "C:/Users/carlo/Desktop/WISE_testStorage_DownloadingFolder";
    public string WebHdfsBaseUrl { get; set; } = "http://localhost:9870";
    public string DataNodeWebHdfsBaseUrl { get; set; } = "http://localhost:9864";
    public string NameNodeRpcAddress { get; set; } = "localhost:50070";
    public string HdfsFolderPath { get; set; } = "/WISE/";
    public string HdfsUser { get; set; } = "WISE";
}

public sealed class PolarisOptions
{
    public bool Enabled { get; set; } = false;
    public string IpAddress { get; set; } = "192.168.99.57";
    public string GCodeDirectory { get; set; } = "/home/polaris/PolarisServer/usr/gcode";
    public string LocalGCodeFolderPath { get; set; } = "C:/Users/carlo/Desktop/WISE_testStorage_UploadingFolder";
}

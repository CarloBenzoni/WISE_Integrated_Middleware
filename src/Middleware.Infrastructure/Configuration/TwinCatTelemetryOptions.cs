using Middleware.Domain.Entities;

namespace Middleware.Infrastructure.Configuration;

public sealed class TwinCatTelemetryOptions
{
    // Initial state for MiddlewareCommand mode. In PlcSymbol mode, the PLC Boolean is authoritative.
    public bool Enabled { get; set; }
    public string EnableMode { get; set; } = "MiddlewareCommand";
    public string EnableSymbol { get; set; } = string.Empty;
    public int EnablePollingIntervalMs { get; set; } = 1000;
    public string SourceId { get; set; } = "DALP-TwinCAT";
    public string RemoteAmsNetId { get; set; } = string.Empty;
    public int AdsPort { get; set; } = 851;
    public List<TwinCatPollingGroupOptions> Groups { get; set; } = [];
}

public sealed class TwinCatPollingGroupOptions
{
    public string Name { get; set; } = string.Empty;
    public int PollingIntervalMs { get; set; } = 1000;
    public List<TwinCatVariableOptions> Variables { get; set; } = [];
}

public sealed class TwinCatVariableOptions
{
    public string Key { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string DataType { get; set; } = "LREAL";

    public TwinCatTelemetryVariable ToDomain() => new(Key, Symbol, DataType);
}

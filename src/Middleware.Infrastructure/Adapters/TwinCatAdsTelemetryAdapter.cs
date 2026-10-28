using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Adapters;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using TwinCAT.Ads;

namespace Middleware.Infrastructure.Adapters;

public sealed class TwinCatAdsTelemetryAdapter(
    IOptions<TwinCatTelemetryOptions> options,
    ILogger<TwinCatAdsTelemetryAdapter> logger) : ITwinCatTelemetryAdapter, IDisposable
{
    private readonly TwinCatTelemetryOptions settings = options.Value;
    private readonly SemaphoreSlim clientLock = new(1, 1);
    private AdsClient? client;

    public async Task<bool> ReadBooleanAsync(string symbol, CancellationToken cancellationToken)
    {
        await clientLock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            cancellationToken.ThrowIfCancellationRequested();
            var value = client!.ReadValue(symbol, typeof(bool));
            return value is bool enabled
                ? enabled
                : throw new InvalidOperationException($"TwinCAT ADS symbol '{symbol}' did not return a BOOL value.");
        }
        finally
        {
            clientLock.Release();
        }
    }

    public async Task<TwinCatTelemetrySample> ReadGroupAsync(
        string sourceId,
        string groupId,
        IReadOnlyCollection<TwinCatTelemetryVariable> variables,
        CancellationToken cancellationToken)
    {
        await clientLock.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();

            var values = new Dictionary<string, PlcTelemetryValue>(StringComparer.OrdinalIgnoreCase);
            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var variable in variables)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var value = client!.ReadValue(variable.Symbol, ResolveClrType(variable.DataType));
                    values[variable.Key] = MapValue(value, variable.DataType);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    errors[variable.Key] = exception.Message;
                    logger.LogWarning(
                        exception,
                        "Could not read TwinCAT ADS symbol {Symbol} ({VariableKey}) in group {GroupId}.",
                        variable.Symbol,
                        variable.Key,
                        groupId);
                }
            }

            var observedAtUtc = DateTimeOffset.UtcNow;
            logger.LogDebug(
                "Read TwinCAT group {GroupId}: {ValueCount} value(s), {ErrorCount} error(s).",
                groupId,
                values.Count,
                errors.Count);

            return new TwinCatTelemetrySample(
                sourceId,
                groupId,
                values,
                errors,
                observedAtUtc,
                observedAtUtc);
        }
        finally
        {
            clientLock.Release();
        }
    }

    public void Dispose()
    {
        client?.Dispose();
        clientLock.Dispose();
    }

    private void EnsureConnected()
    {
        if (client is { IsConnected: true })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.RemoteAmsNetId))
        {
            throw new InvalidOperationException("TwinCatTelemetry.RemoteAmsNetId must be configured.");
        }

        client ??= new AdsClient();
        client.Connect(AmsNetId.Parse(settings.RemoteAmsNetId), settings.AdsPort);

        logger.LogInformation(
            "Connected to TwinCAT ADS target {AmsNetId}, port {AdsPort}.",
            settings.RemoteAmsNetId,
            settings.AdsPort);
    }

    private static Type ResolveClrType(string dataType) => dataType.Trim().ToUpperInvariant() switch
    {
        "BOOL" => typeof(bool),
        "BYTE" => typeof(byte),
        "SINT" => typeof(sbyte),
        "USINT" => typeof(byte),
        "INT" => typeof(short),
        "UINT" => typeof(ushort),
        "DINT" => typeof(int),
        "UDINT" => typeof(uint),
        "LINT" => typeof(long),
        "ULINT" => typeof(ulong),
        "REAL" => typeof(float),
        "LREAL" => typeof(double),
        "STRING" => typeof(string),
        _ => throw new InvalidOperationException($"Unsupported TwinCAT ADS data type '{dataType}'.")
    };

    private static PlcTelemetryValue MapValue(object? value, string dataType)
    {
        var rawValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        double? numericValue = value switch
        {
            bool boolean => boolean ? 1d : 0d,
            IConvertible convertible when IsNumericType(value.GetType()) => convertible.ToDouble(CultureInfo.InvariantCulture),
            _ => null
        };

        return new PlcTelemetryValue(rawValue, numericValue, dataType.Trim().ToLowerInvariant());
    }

    private static bool IsNumericType(Type type) =>
        Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte or
            TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or
            TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
}

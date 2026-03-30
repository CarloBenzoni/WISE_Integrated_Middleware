using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public sealed class TelemetryController(
    IPlcTelemetrySampleRepository repository,
    IOptions<MiddlewareOptions> options) : ControllerBase
{
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(7);

    [HttpGet("variables")]
    public async Task<IActionResult> GetVariables(CancellationToken cancellationToken)
    {
        var latestSample = await repository.GetLatestAsync(cancellationToken);
        return Ok(GetVariableDefinitions(latestSample, options.Value.OpcUa));
    }

    [HttpGet("series")]
    public async Task<IActionResult> GetSeries(
        [FromQuery] string variableKey,
        [FromQuery] DateTimeOffset? startUtc,
        [FromQuery] DateTimeOffset? endUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(variableKey))
        {
            return BadRequest(new { message = "variableKey is required." });
        }

        var range = NormalizeRange(startUtc, endUtc);
        if (range is null)
        {
            return BadRequest(new { message = "Invalid time range. Maximum allowed range is 7 days." });
        }

        var latestSample = await repository.GetLatestAsync(cancellationToken);
        var definition = GetVariableDefinitions(latestSample, options.Value.OpcUa)
            .FirstOrDefault(item => string.Equals(item.Key, variableKey, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return NotFound(new { message = $"Unknown variable: {variableKey}" });
        }

        var samples = await repository.QueryRangeAsync(range.StartUtc, range.EndUtc, cancellationToken);
        var points = samples.Select(sample => MapPoint(sample, definition.Key)).ToList();

        return Ok(new TelemetrySeriesResponse(
            Variable: definition,
            StartUtc: range.StartUtc,
            EndUtc: range.EndUtc,
            Count: points.Count,
            Points: points));
    }

    [HttpGet("series/csv")]
    public async Task<IActionResult> DownloadCsv(
        [FromQuery] string variableKey,
        [FromQuery] DateTimeOffset? startUtc,
        [FromQuery] DateTimeOffset? endUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(variableKey))
        {
            return BadRequest(new { message = "variableKey is required." });
        }

        var range = NormalizeRange(startUtc, endUtc);
        if (range is null)
        {
            return BadRequest(new { message = "Invalid time range. Maximum allowed range is 7 days." });
        }

        var latestSample = await repository.GetLatestAsync(cancellationToken);
        var definition = GetVariableDefinitions(latestSample, options.Value.OpcUa)
            .FirstOrDefault(item => string.Equals(item.Key, variableKey, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return NotFound(new { message = $"Unknown variable: {variableKey}" });
        }

        var samples = await repository.QueryRangeAsync(range.StartUtc, range.EndUtc, cancellationToken);
        var points = samples.Select(sample => MapPoint(sample, definition.Key)).ToList();

        var builder = new StringBuilder();
        builder.AppendLine("timestamp_utc,raw_value,numeric_value");
        foreach (var point in points)
        {
            builder.Append(point.TimestampUtc.ToString("O", CultureInfo.InvariantCulture));
            builder.Append(',');
            builder.Append(EscapeCsv(point.RawValue));
            builder.Append(',');
            builder.AppendLine(point.NumericValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var fileName = $"telemetry_{definition.Key}_{range.StartUtc:yyyyMMddHHmm}_{range.EndUtc:yyyyMMddHHmm}.csv";
        return File(bytes, "text/csv", fileName);
    }

    private static IReadOnlyCollection<TelemetryVariableDefinition> GetVariableDefinitions(
        PlcTelemetrySample? latestSample,
        OpcUaOptions options)
    {
        if (latestSample is not null)
        {
            return
            [
                new TelemetryVariableDefinition(
                    "axis_position",
                    "axis_position",
                    "number"),
                new TelemetryVariableDefinition(
                    "machine_status",
                    "machine_status",
                    bool.TryParse(latestSample.MachineStatus, out _) ? "boolean" : "string"),
                new TelemetryVariableDefinition(
                    "alarm_code",
                    "alarm_code",
                    "number")
            ];
        }

        return
        [
            new TelemetryVariableDefinition(
                "axis_position",
                options.BrowseNames.AxisPositionBrowseName,
                "number"),
            new TelemetryVariableDefinition(
                "machine_status",
                options.BrowseNames.MachineStatusBrowseName,
                "boolean"),
            new TelemetryVariableDefinition(
                "alarm_code",
                options.BrowseNames.AlarmCodeBrowseName,
                "number")
        ];
    }

    private static TelemetryDataPoint MapPoint(PlcTelemetrySample sample, string variableKey)
    {
        return variableKey switch
        {
            "axis_position" => new TelemetryDataPoint(
                sample.IngestedAtUtc,
                sample.AxisPosition.ToString(CultureInfo.InvariantCulture),
                sample.AxisPosition),
            "machine_status" => new TelemetryDataPoint(
                sample.IngestedAtUtc,
                sample.MachineStatus,
                TryConvertBoolean(sample.MachineStatus)),
            "alarm_code" => new TelemetryDataPoint(
                sample.IngestedAtUtc,
                sample.AlarmCode.ToString(CultureInfo.InvariantCulture),
                sample.AlarmCode),
            _ => throw new InvalidOperationException($"Unsupported variable key: {variableKey}")
        };
    }

    private static double? TryConvertBoolean(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue ? 1d : 0d;
        }

        return null;
    }

    private static TimeRange? NormalizeRange(DateTimeOffset? startUtc, DateTimeOffset? endUtc)
    {
        var resolvedEnd = endUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var resolvedStart = startUtc?.ToUniversalTime() ?? resolvedEnd.AddDays(-1);

        if (resolvedStart >= resolvedEnd)
        {
            return null;
        }

        if (resolvedEnd - resolvedStart > MaxRange)
        {
            return null;
        }

        return new TimeRange(resolvedStart, resolvedEnd);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return value;
    }

    private sealed record TimeRange(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
}

public sealed record TelemetrySeriesResponse(
    TelemetryVariableDefinition Variable,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int Count,
    IReadOnlyCollection<TelemetryDataPoint> Points);

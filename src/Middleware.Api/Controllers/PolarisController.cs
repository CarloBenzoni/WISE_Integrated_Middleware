using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Middleware.Application.Services;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Api.Controllers;

[ApiController]
[Route("api/polaris")]
public sealed class PolarisController(
    PolarisGCodeService polarisGCodeService,
    PolarisInfoService polarisInfoService,
    IOptions<MiddlewareOptions> options) : ControllerBase
{
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        return Ok(new
        {
            enabled = options.Value.Polaris.Enabled,
            ipAddress = options.Value.Polaris.IpAddress,
            gCodeDirectory = options.Value.Polaris.GCodeDirectory,
            localGCodeFolderPath = options.Value.Polaris.LocalGCodeFolderPath
        });
    }

    [HttpGet("info")]
    public async Task<IActionResult> GetInfo(CancellationToken cancellationToken)
    {
        var result = await polarisInfoService.ReadSystemInfoAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("gcode/status")]
    public async Task<IActionResult> GetGCodeStatus(CancellationToken cancellationToken)
    {
        var result = await polarisGCodeService.ReadStatusAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("upload-and-run")]
    public async Task<IActionResult> UploadAndRun(
        [FromBody] PolarisUploadAndRunRequest request,
        CancellationToken cancellationToken)
    {
        var localFilePath = ResolveLocalFilePath(request.LocalFilePath);
        var remoteFileName = string.IsNullOrWhiteSpace(request.RemoteFileName)
            ? Path.GetFileName(localFilePath)
            : request.RemoteFileName;

        var result = await polarisGCodeService.UploadAndRunAsync(
            localFilePath,
            remoteFileName,
            options.Value.Polaris.IpAddress,
            options.Value.Polaris.GCodeDirectory,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("run-remote")]
    public async Task<IActionResult> RunRemote(
        [FromBody] PolarisRunRemoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await polarisGCodeService.RunRemoteAsync(
            request.RemoteFileName,
            options.Value.Polaris.IpAddress,
            options.Value.Polaris.GCodeDirectory,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("enable-axis")]
    public async Task<IActionResult> EnableAxis(
        [FromBody] PolarisEnableAxisRequest request,
        CancellationToken cancellationToken)
    {
        var result = await polarisGCodeService.EnableAxisAsync(
            request.AxisName,
            options.Value.Polaris.IpAddress,
            cancellationToken);

        return Ok(result);
    }

    private string ResolveLocalFilePath(string localFilePath)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new ArgumentException("Local file path is required.", nameof(localFilePath));
        }

        return Path.IsPathRooted(localFilePath)
            ? localFilePath
            : Path.Combine(options.Value.Polaris.LocalGCodeFolderPath, localFilePath);
    }
}

public sealed record PolarisUploadAndRunRequest(string LocalFilePath, string? RemoteFileName);

public sealed record PolarisRunRemoteRequest(string RemoteFileName);

public sealed record PolarisEnableAxisRequest(string AxisName);

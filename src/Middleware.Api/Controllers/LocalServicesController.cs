using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Middleware.Api.LocalServices;

namespace Middleware.Api.Controllers;

[ApiController]
[Route("api/local-services")]
public sealed class LocalServicesController(LocalServiceManager manager) : ControllerBase
{
    private static readonly string Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    [HttpGet]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        if (!IsLocalRequest()) return Unavailable();
        var status = await manager.RefreshAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            status.Installed, status.CheckedAt, status.Services, status.Operation,
            status.Error, status.Message, ControlToken = Token
        });
    }

    [HttpPost("{service}/{operation}")]
    public IActionResult Command(string service, string operation)
    {
        if (!IsLocalRequest()) return Unavailable();
        var supplied = Request.Headers["x-wise-service-token"].ToString();
        var bytes = Encoding.ASCII.GetBytes(supplied);
        var expected = Encoding.ASCII.GetBytes(Token);
        if (bytes.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(bytes, expected))
            return StatusCode(403, new { message = "Refresh service status before sending a command." });
        try
        {
            Response.Headers.CacheControl = "no-store";
            return Accepted(manager.Request(operation, service));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    private bool IsLocalRequest()
    {
        if (!OperatingSystem.IsWindows() ||
            string.Equals(Environment.GetEnvironmentVariable("LOCAL_SERVICE_MANAGER_ENABLED"), "false", StringComparison.OrdinalIgnoreCase))
            return false;
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote))
            return false;
        if (Request.Host.Host is not ("localhost" or "127.0.0.1" or "::1" or "[::1]"))
            return false;
        if (Request.Headers.TryGetValue("Origin", out var origin))
        {
            if (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri) ||
                originUri.Scheme != "http" ||
                originUri.Host is not ("localhost" or "127.0.0.1" or "::1") ||
                originUri.Port != 5173 && originUri.Port != Request.Host.Port)
                return false;
        }
        return true;
    }

    private IActionResult Unavailable() => StatusCode(503,
        new { message = "Service management is available only on the local Windows workstation." });
}

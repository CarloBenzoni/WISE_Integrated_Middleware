using Microsoft.AspNetCore.Mvc;
using Middleware.Infrastructure.Services;

namespace Middleware.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemStatusController(
    MiddlewareDiagnosticsService diagnosticsService) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var snapshot = await diagnosticsService.GetSnapshotAsync(cancellationToken);
        return Ok(snapshot);
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Middleware.Infrastructure.Configuration;

namespace Middleware.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController(IOptions<MiddlewareOptions> options) : ControllerBase
{
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new
        {
            status = "ok",
            middleware = "WISE Integrated Middleware",
            plcId = options.Value.OpcUa.PlcId,
            opcUaEndpoint = options.Value.OpcUa.EndpointUrl,
            mongoDatabase = options.Value.Mongo.Database,
            plcTelemetryCollection = options.Value.Mongo.PlcTelemetryCollection,
            fileMetadataCollection = options.Value.Mongo.FileMetadataCollection,
            webHdfsBaseUrl = options.Value.Storage.WebHdfsBaseUrl,
            dataNodeWebHdfsBaseUrl = options.Value.Storage.DataNodeWebHdfsBaseUrl,
            nameNodeRpcAddress = options.Value.Storage.NameNodeRpcAddress,
            polarisEnabled = options.Value.Polaris.Enabled,
            polarisIpAddress = options.Value.Polaris.IpAddress,
            polarisGCodeDirectory = options.Value.Polaris.GCodeDirectory,
            timestampUtc = DateTimeOffset.UtcNow
        });
    }
}

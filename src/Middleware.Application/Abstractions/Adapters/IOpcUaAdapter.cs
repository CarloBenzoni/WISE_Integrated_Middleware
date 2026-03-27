using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Adapters;

public interface IOpcUaAdapter
{
    Task<PlcTelemetrySample> ReadCurrentSampleAsync(CancellationToken cancellationToken);
}

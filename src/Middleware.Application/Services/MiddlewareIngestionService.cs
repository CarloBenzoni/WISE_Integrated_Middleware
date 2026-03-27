using Middleware.Application.Abstractions.Adapters;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;

namespace Middleware.Application.Services;

public sealed class MiddlewareIngestionService(
    IOpcUaAdapter opcUaAdapter,
    IPlcTelemetrySampleRepository sampleRepository)
{
    public async Task<PlcTelemetrySample> IngestAsync(CancellationToken cancellationToken)
    {
        var sample = await opcUaAdapter.ReadCurrentSampleAsync(cancellationToken);
        await sampleRepository.InsertAsync(sample, cancellationToken);

        return sample;
    }
}

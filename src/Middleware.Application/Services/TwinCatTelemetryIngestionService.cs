using Middleware.Application.Abstractions.Adapters;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;

namespace Middleware.Application.Services;

public sealed class TwinCatTelemetryIngestionService(
    ITwinCatTelemetryAdapter adapter,
    ITwinCatTelemetryRepository repository)
{
    public async Task<TwinCatTelemetrySample> IngestGroupAsync(
        string sourceId,
        string groupId,
        IReadOnlyCollection<TwinCatTelemetryVariable> variables,
        CancellationToken cancellationToken)
    {
        var sample = await adapter.ReadGroupAsync(sourceId, groupId, variables, cancellationToken);
        sample = sample with { IngestedAtUtc = DateTimeOffset.UtcNow };
        await repository.InsertAsync(sample, cancellationToken);
        return sample;
    }
}

using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Adapters;

public interface ITwinCatTelemetryAdapter
{
    Task<bool> ReadBooleanAsync(string symbol, CancellationToken cancellationToken);

    Task<TwinCatTelemetrySample> ReadGroupAsync(
        string sourceId,
        string groupId,
        IReadOnlyCollection<TwinCatTelemetryVariable> variables,
        CancellationToken cancellationToken);
}

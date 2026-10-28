using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Repositories;

public interface ITwinCatTelemetryRepository
{
    Task InsertAsync(TwinCatTelemetrySample sample, CancellationToken cancellationToken);
}

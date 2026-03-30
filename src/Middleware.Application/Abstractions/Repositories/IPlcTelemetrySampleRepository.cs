using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Repositories;

public interface IPlcTelemetrySampleRepository
{
    Task InsertAsync(PlcTelemetrySample sample, CancellationToken cancellationToken);
    Task<PlcTelemetrySample?> GetLatestAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<PlcTelemetrySample>> QueryRangeAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken);
}

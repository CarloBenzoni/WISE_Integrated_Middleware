using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Repositories;

public interface IPlcTelemetrySampleRepository
{
    Task InsertAsync(PlcTelemetrySample sample, CancellationToken cancellationToken);
    Task<PlcTelemetrySample?> GetLatestAsync(CancellationToken cancellationToken);
}

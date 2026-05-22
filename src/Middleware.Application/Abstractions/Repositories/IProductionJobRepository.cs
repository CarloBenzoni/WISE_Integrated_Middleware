using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Repositories;

public interface IProductionJobRepository
{
    Task InsertAsync(ProductionJob job, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductionJob>> ListAsync(ProductionJobQuery query, CancellationToken cancellationToken);
    Task<ProductionJob?> GetByJobIdAsync(string jobId, CancellationToken cancellationToken);
    Task<ProductionJob?> ClaimNextQueuedAsync(CancellationToken cancellationToken);
    Task UpdateAsync(ProductionJob job, CancellationToken cancellationToken);
}

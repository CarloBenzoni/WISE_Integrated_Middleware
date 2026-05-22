using Middleware.Domain.Entities;

namespace Middleware.Application.Abstractions.Repositories;

public interface IProductionDesignRepository
{
    Task InsertAsync(ProductionDesign design, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductionDesign>> ListAsync(ProductionDesignQuery query, CancellationToken cancellationToken);
    Task<ProductionDesign?> GetByDesignIdAsync(string designId, CancellationToken cancellationToken);
}

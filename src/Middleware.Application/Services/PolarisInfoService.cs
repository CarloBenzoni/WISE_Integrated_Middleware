using Middleware.Application.Abstractions.Adapters;
using Middleware.Domain.Entities;

namespace Middleware.Application.Services;

public sealed class PolarisInfoService(IPolarisGCodeAdapter polarisGCodeAdapter)
{
    public Task<PolarisSystemInfo> ReadSystemInfoAsync(CancellationToken cancellationToken)
    {
        return polarisGCodeAdapter.ReadSystemInfoAsync(cancellationToken);
    }
}

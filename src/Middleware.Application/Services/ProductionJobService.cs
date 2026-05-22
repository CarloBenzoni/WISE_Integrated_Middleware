using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;

namespace Middleware.Application.Services;

public sealed class ProductionJobService(
    IProductionDesignRepository designRepository,
    IProductionJobRepository jobRepository)
{
    public async Task<ProductionJob> QueueAsync(
        string designId,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(designId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        var design = await designRepository.GetByDesignIdAsync(designId, cancellationToken);
        if (design is null)
        {
            throw new FileNotFoundException($"Production design not found: {designId}");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var job = new ProductionJob(
            JobId: Guid.NewGuid().ToString("N"),
            DesignId: design.DesignId,
            DesignName: design.DesignName,
            UseCase: design.UseCase,
            RequestedBy: requestedBy.Trim(),
            RequestedAtUtc: timestamp,
            Status: ProductionJobStatuses.Queued,
            CurrentStepSequence: null,
            CurrentStepName: null,
            StartedAtUtc: null,
            CompletedAtUtc: null,
            FailureReason: null,
            Events:
            [
                new ProductionJobEvent(
                    OccurredAtUtc: timestamp,
                    Level: "info",
                    Message: "Job queued.",
                    StepSequence: null,
                    StepName: null)
            ]);

        await jobRepository.InsertAsync(job, cancellationToken);
        return job;
    }

    public Task<IReadOnlyCollection<ProductionJob>> ListAsync(
        ProductionJobQuery query,
        CancellationToken cancellationToken)
    {
        return jobRepository.ListAsync(query, cancellationToken);
    }

    public Task<ProductionJob?> GetByJobIdAsync(string jobId, CancellationToken cancellationToken)
    {
        return jobRepository.GetByJobIdAsync(jobId, cancellationToken);
    }
}

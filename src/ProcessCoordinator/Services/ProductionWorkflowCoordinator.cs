using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Application.Services;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;

namespace ProcessCoordinator.Services;

public sealed class ProductionWorkflowCoordinator(
    IProductionDesignRepository designRepository,
    IProductionJobRepository jobRepository,
    FileUploadService fileUploadService,
    PolarisGCodeService polarisGCodeService,
    PolarisInfoService polarisInfoService,
    IOptions<MiddlewareOptions> options,
    ILogger<ProductionWorkflowCoordinator> logger)
{
    public async Task<bool> TryExecuteNextJobAsync(CancellationToken cancellationToken)
    {
        var claimedJob = await jobRepository.ClaimNextQueuedAsync(cancellationToken);
        if (claimedJob is null)
        {
            return false;
        }

        var workingDirectory = Path.Combine(Path.GetTempPath(), "wise-production-jobs", claimedJob.JobId);
        Directory.CreateDirectory(workingDirectory);

        try
        {
            claimedJob = await AppendEventAndPersistAsync(
                claimedJob with
                {
                    Status = ProductionJobStatuses.Validating,
                    StartedAtUtc = DateTimeOffset.UtcNow
                },
                "info",
                "Job claimed by ProcessCoordinator.",
                null,
                null,
                cancellationToken);

            var design = await designRepository.GetByDesignIdAsync(claimedJob.DesignId, cancellationToken);
            if (design is null)
            {
                await FailJobAsync(claimedJob, "Referenced production design was not found.", null, null, cancellationToken);
                return true;
            }

            var systemInfo = await polarisInfoService.ReadSystemInfoAsync(cancellationToken);
            if (!string.Equals(systemInfo.PolarisCurrentStatus, "Running", StringComparison.OrdinalIgnoreCase))
            {
                await FailJobAsync(
                    claimedJob,
                    $"Polaris is not ready. Current status: {systemInfo.PolarisCurrentStatus}.",
                    null,
                    null,
                    cancellationToken);
                return true;
            }

            claimedJob = await AppendEventAndPersistAsync(
                claimedJob with { Status = ProductionJobStatuses.Running },
                "info",
                "Machine readiness check passed.",
                null,
                null,
                cancellationToken);

            foreach (var step in design.ProcessSteps.OrderBy(item => item.Sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();

                claimedJob = await AppendEventAndPersistAsync(
                    claimedJob with
                    {
                        CurrentStepSequence = step.Sequence,
                        CurrentStepName = step.ProcessName
                    },
                    "info",
                    $"Starting process step {step.Sequence}: {step.ProcessName}.",
                    step.Sequence,
                    step.ProcessName,
                    cancellationToken);

                if (!step.RequiresMachineExecution)
                {
                    claimedJob = await AppendEventAndPersistAsync(
                        claimedJob,
                        "info",
                        $"Skipping non-machine step {step.Sequence}: {step.ProcessName}.",
                        step.Sequence,
                        step.ProcessName,
                        cancellationToken);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(step.PartProgramDocumentId))
                {
                    await FailJobAsync(
                        claimedJob,
                        $"No part program linked to step {step.Sequence} ({step.ProcessName}).",
                        step.Sequence,
                        step.ProcessName,
                        cancellationToken);
                    return true;
                }

                var fileContent = await fileUploadService.DownloadAsync(step.PartProgramDocumentId, cancellationToken);
                var localFilePath = Path.Combine(
                    workingDirectory,
                    $"{step.Sequence:D2}_{Path.GetFileName(fileContent.FileName)}");

                await File.WriteAllBytesAsync(localFilePath, fileContent.Content, cancellationToken);

                var remoteFileName = BuildRemoteFileName(claimedJob.JobId, step.Sequence, fileContent.FileName);

                logger.LogInformation(
                    "Executing production job {JobId}, step {StepSequence}, process {ProcessName}, remote file {RemoteFileName}.",
                    claimedJob.JobId,
                    step.Sequence,
                    step.ProcessName,
                    remoteFileName);

                await polarisGCodeService.UploadAsync(localFilePath, remoteFileName, cancellationToken);
                await polarisGCodeService.RunRemoteAsync(
                    remoteFileName,
                    options.Value.Polaris.IpAddress,
                    options.Value.Polaris.GCodeDirectory,
                    cancellationToken);

                await WaitForStepCompletionAsync(claimedJob, step, cancellationToken);

                claimedJob = await AppendEventAndPersistAsync(
                    claimedJob,
                    "info",
                    $"Completed process step {step.Sequence}: {step.ProcessName}.",
                    step.Sequence,
                    step.ProcessName,
                    cancellationToken);
            }

            claimedJob = await AppendEventAndPersistAsync(
                claimedJob with
                {
                    Status = ProductionJobStatuses.Completed,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    FailureReason = null,
                    CurrentStepSequence = null,
                    CurrentStepName = null
                },
                "info",
                "Production job completed successfully.",
                null,
                null,
                cancellationToken);

            logger.LogInformation("Production job {JobId} completed successfully.", claimedJob.JobId);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Production job {JobId} failed.", claimedJob.JobId);
            await FailJobAsync(
                claimedJob,
                exception.Message,
                claimedJob.CurrentStepSequence,
                claimedJob.CurrentStepName,
                cancellationToken);
            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workingDirectory))
                {
                    Directory.Delete(workingDirectory, recursive: true);
                }
            }
            catch (Exception cleanupException)
            {
                logger.LogWarning(cleanupException, "Failed to clean temporary coordinator directory {WorkingDirectory}.", workingDirectory);
            }
        }
    }

    private async Task WaitForStepCompletionAsync(
        ProductionJob job,
        ProductionProcessStepDefinition step,
        CancellationToken cancellationToken)
    {
        var coordinatorOptions = options.Value.ProcessCoordinator;
        var pollingDelay = TimeSpan.FromMilliseconds(Math.Max(250, coordinatorOptions.GCodeStatusPollingIntervalMs));
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(Math.Max(1000, coordinatorOptions.StepCompletionTimeoutMs));

        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = await polarisGCodeService.ReadStatusAsync(cancellationToken);
            logger.LogInformation(
                "Production job {JobId}, step {StepSequence}: Polaris G-code state {State} ({StateCode}).",
                job.JobId,
                step.Sequence,
                status.State,
                status.StateCode);

            if (string.Equals(status.State, "Aborted", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(status.Error))
            {
                throw new InvalidOperationException(
                    $"Polaris aborted step {step.Sequence} ({step.ProcessName}). {status.Error}".Trim());
            }

            if (status.State is "Ready" or "Stopped")
            {
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out while waiting for step {step.Sequence} ({step.ProcessName}) to complete.");
            }

            await Task.Delay(pollingDelay, cancellationToken);
        }
    }

    private async Task FailJobAsync(
        ProductionJob job,
        string failureReason,
        int? stepSequence,
        string? stepName,
        CancellationToken cancellationToken)
    {
        await AppendEventAndPersistAsync(
            job with
            {
                Status = ProductionJobStatuses.Failed,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                FailureReason = failureReason
            },
            "error",
            failureReason,
            stepSequence,
            stepName,
            cancellationToken);
    }

    private async Task<ProductionJob> AppendEventAndPersistAsync(
        ProductionJob job,
        string level,
        string message,
        int? stepSequence,
        string? stepName,
        CancellationToken cancellationToken)
    {
        var updatedJob = job with
        {
            Events = job.Events
                .Append(new ProductionJobEvent(
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Level: level,
                    Message: message,
                    StepSequence: stepSequence,
                    StepName: stepName))
                .ToList()
        };

        await jobRepository.UpdateAsync(updatedJob, cancellationToken);
        return updatedJob;
    }

    private static string BuildRemoteFileName(string jobId, int sequence, string originalFileName)
    {
        var cleanFileName = Path.GetFileName(originalFileName);
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(cleanFileName
            .Select(character => invalidCharacters.Contains(character) || character == ' '
                ? '_'
                : character)
            .ToArray());

        return $"{jobId}_{sequence:D2}_{sanitized}";
    }
}

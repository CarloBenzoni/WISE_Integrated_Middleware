using Microsoft.AspNetCore.Mvc;
using Middleware.Application.Services;
using Middleware.Domain.Entities;

namespace Middleware.Api.Controllers;

[ApiController]
[Route("api/production")]
public sealed class ProductionController(
    ProductionDesignService productionDesignService,
    ProductionJobService productionJobService) : ControllerBase
{
    [HttpGet("designs")]
    public async Task<IActionResult> ListDesigns(
        [FromQuery] string? search,
        [FromQuery] string? useCase,
        [FromQuery] string? designedBy,
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var designs = await productionDesignService.ListAsync(
            new ProductionDesignQuery(search, useCase, designedBy, fromUtc, toUtc),
            cancellationToken);

        return Ok(designs);
    }

    [HttpGet("designs/{designId}")]
    public async Task<IActionResult> GetDesign(string designId, CancellationToken cancellationToken)
    {
        var design = await productionDesignService.GetByDesignIdAsync(designId, cancellationToken);
        if (design is null)
        {
            return NotFound(new { message = $"Production design not found: {designId}" });
        }

        return Ok(design);
    }

    [HttpPost("designs")]
    public async Task<IActionResult> CreateDesign(
        [FromBody] CreateProductionDesignRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProcessSteps is null || request.ProcessSteps.Count == 0)
        {
            return BadRequest(new { message = "At least one process step is required." });
        }

        var designId = string.IsNullOrWhiteSpace(request.DesignId)
            ? Guid.NewGuid().ToString("N")
            : request.DesignId.Trim();

        var design = new ProductionDesign(
            DesignId: designId,
            DesignName: request.DesignName.Trim(),
            UseCase: request.UseCase.Trim(),
            ProductIdentifier: request.ProductIdentifier.Trim(),
            DesignedBy: request.DesignedBy.Trim(),
            DesignedAtUtc: request.DesignedAtUtc ?? DateTimeOffset.UtcNow,
            Notes: request.Notes,
            RelatedDocuments: (request.RelatedDocuments ?? [])
                .Select(document => new ProductionDocumentLink(
                    document.DocumentId.Trim(),
                    document.Role.Trim(),
                    document.Category,
                    document.FileName))
                .ToList(),
            ProcessSteps: request.ProcessSteps
                .OrderBy(step => step.Sequence)
                .Select(step => new ProductionProcessStepDefinition(
                    Sequence: step.Sequence,
                    ProcessName: step.ProcessName.Trim(),
                    ProcessLabel: string.IsNullOrWhiteSpace(step.ProcessLabel)
                        ? step.ProcessName.Trim()
                        : step.ProcessLabel.Trim(),
                    RequiresMachineExecution: step.RequiresMachineExecution,
                    SelectedRecipeName: step.SelectedRecipeName,
                    SelectedRecipeIndex: step.SelectedRecipeIndex,
                    PartProgramDocumentId: string.IsNullOrWhiteSpace(step.PartProgramDocumentId)
                        ? null
                        : step.PartProgramDocumentId.Trim(),
                    PartProgramFileName: step.PartProgramFileName,
                    SupportingDocumentIds: (step.SupportingDocumentIds ?? [])
                        .Where(documentId => !string.IsNullOrWhiteSpace(documentId))
                        .Select(documentId => documentId.Trim())
                        .ToList(),
                    Parameters: (step.Parameters ?? [])
                        .Select(parameter => new ProductionParameterSnapshot(
                            parameter.Key.Trim(),
                            parameter.Label.Trim(),
                            parameter.Unit,
                            parameter.Value))
                        .ToList()))
                .ToList());

        var persistedDesign = await productionDesignService.CreateAsync(design, cancellationToken);
        return Ok(persistedDesign);
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> ListJobs(
        [FromQuery] string? status,
        [FromQuery] string? designId,
        [FromQuery] string? useCase,
        [FromQuery] string? requestedBy,
        CancellationToken cancellationToken)
    {
        var jobs = await productionJobService.ListAsync(
            new ProductionJobQuery(status, designId, useCase, requestedBy),
            cancellationToken);

        return Ok(jobs);
    }

    [HttpGet("jobs/{jobId}")]
    public async Task<IActionResult> GetJob(string jobId, CancellationToken cancellationToken)
    {
        var job = await productionJobService.GetByJobIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return NotFound(new { message = $"Production job not found: {jobId}" });
        }

        return Ok(job);
    }

    [HttpPost("jobs")]
    public async Task<IActionResult> QueueJob(
        [FromBody] CreateProductionJobRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var job = await productionJobService.QueueAsync(
                request.DesignId.Trim(),
                request.RequestedBy.Trim(),
                cancellationToken);

            return Ok(job);
        }
        catch (FileNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}

public sealed record CreateProductionDesignRequest(
    string? DesignId,
    string DesignName,
    string UseCase,
    string ProductIdentifier,
    string DesignedBy,
    DateTimeOffset? DesignedAtUtc,
    string? Notes,
    IReadOnlyCollection<CreateProductionDocumentLinkRequest>? RelatedDocuments,
    IReadOnlyCollection<CreateProductionProcessStepRequest> ProcessSteps);

public sealed record CreateProductionDocumentLinkRequest(
    string DocumentId,
    string Role,
    string? Category,
    string? FileName);

public sealed record CreateProductionProcessStepRequest(
    int Sequence,
    string ProcessName,
    string? ProcessLabel,
    bool RequiresMachineExecution,
    string? SelectedRecipeName,
    int? SelectedRecipeIndex,
    string? PartProgramDocumentId,
    string? PartProgramFileName,
    IReadOnlyCollection<string>? SupportingDocumentIds,
    IReadOnlyCollection<CreateProductionParameterSnapshotRequest>? Parameters);

public sealed record CreateProductionParameterSnapshotRequest(
    string Key,
    string Label,
    string? Unit,
    string Value);

public sealed record CreateProductionJobRequest(
    string DesignId,
    string RequestedBy);

using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;

namespace Middleware.Application.Services;

public sealed class ProductionDesignService(
    IProductionDesignRepository designRepository,
    IFileArtifactMetadataRepository fileArtifactMetadataRepository)
{
    public async Task<ProductionDesign> CreateAsync(ProductionDesign design, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(design.DesignId);
        ArgumentException.ThrowIfNullOrWhiteSpace(design.DesignName);
        ArgumentException.ThrowIfNullOrWhiteSpace(design.UseCase);
        ArgumentException.ThrowIfNullOrWhiteSpace(design.ProductIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(design.DesignedBy);

        if (design.ProcessSteps.Count == 0)
        {
            throw new InvalidOperationException("At least one process step is required to persist a production design.");
        }

        await ValidateLinkedDocumentsAsync(design, cancellationToken);
        await designRepository.InsertAsync(design, cancellationToken);
        return design;
    }

    public Task<IReadOnlyCollection<ProductionDesign>> ListAsync(
        ProductionDesignQuery query,
        CancellationToken cancellationToken)
    {
        return designRepository.ListAsync(query, cancellationToken);
    }

    public Task<ProductionDesign?> GetByDesignIdAsync(string designId, CancellationToken cancellationToken)
    {
        return designRepository.GetByDesignIdAsync(designId, cancellationToken);
    }

    private async Task ValidateLinkedDocumentsAsync(ProductionDesign design, CancellationToken cancellationToken)
    {
        var linkedDocumentIds = design.RelatedDocuments
            .Select(document => document.DocumentId)
            .Concat(design.ProcessSteps.SelectMany(step => step.SupportingDocumentIds))
            .Concat(design.ProcessSteps
                .Where(step => !string.IsNullOrWhiteSpace(step.PartProgramDocumentId))
                .Select(step => step.PartProgramDocumentId!))
            .Where(documentId => !string.IsNullOrWhiteSpace(documentId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var documentId in linkedDocumentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var linkedDocument = await fileArtifactMetadataRepository.GetByDocumentIdAsync(documentId, cancellationToken);
            if (linkedDocument is null)
            {
                throw new FileNotFoundException($"Linked document not found: {documentId}");
            }
        }
    }
}

namespace Middleware.Domain.Entities;

public sealed record ProductionDesign(
    string DesignId,
    string DesignName,
    string UseCase,
    string ProductIdentifier,
    string DesignedBy,
    DateTimeOffset DesignedAtUtc,
    string? Notes,
    IReadOnlyCollection<ProductionDocumentLink> RelatedDocuments,
    IReadOnlyCollection<ProductionProcessStepDefinition> ProcessSteps);

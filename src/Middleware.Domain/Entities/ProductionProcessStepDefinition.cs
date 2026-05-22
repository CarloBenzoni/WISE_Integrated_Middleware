namespace Middleware.Domain.Entities;

public sealed record ProductionProcessStepDefinition(
    int Sequence,
    string ProcessName,
    string ProcessLabel,
    bool RequiresMachineExecution,
    string? SelectedRecipeName,
    int? SelectedRecipeIndex,
    string? PartProgramDocumentId,
    string? PartProgramFileName,
    IReadOnlyCollection<string> SupportingDocumentIds,
    IReadOnlyCollection<ProductionParameterSnapshot> Parameters);

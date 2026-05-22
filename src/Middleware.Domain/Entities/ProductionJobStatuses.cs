namespace Middleware.Domain.Entities;

public static class ProductionJobStatuses
{
    public const string Queued = "Queued";
    public const string Validating = "Validating";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

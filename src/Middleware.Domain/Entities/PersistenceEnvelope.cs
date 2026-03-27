namespace Middleware.Domain.Entities;

public sealed record PersistenceEnvelope<TPayload>(
    string RecordType,
    string SourceType,
    string SourceId,
    DateTimeOffset TimestampUtc,
    DateTimeOffset IngestedAtUtc,
    TPayload Payload);

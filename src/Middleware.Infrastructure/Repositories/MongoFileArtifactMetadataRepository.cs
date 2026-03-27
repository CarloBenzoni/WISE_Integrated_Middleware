using Microsoft.Extensions.Options;
using Middleware.Application.Abstractions.Repositories;
using Middleware.Domain.Entities;
using Middleware.Infrastructure.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Middleware.Infrastructure.Repositories;

public sealed class MongoFileArtifactMetadataRepository : IFileArtifactMetadataRepository
{
    private readonly IMongoCollection<FileArtifactEnvelopeDocument> collection;

    public MongoFileArtifactMetadataRepository(IOptions<MiddlewareOptions> options)
    {
        var mongoOptions = options.Value.Mongo;
        var client = new MongoClient(mongoOptions.ConnectionString);
        var database = client.GetDatabase(mongoOptions.Database);
        collection = database.GetCollection<FileArtifactEnvelopeDocument>(mongoOptions.FileMetadataCollection);
    }

    public async Task InsertAsync(FileArtifactMetadata metadata, CancellationToken cancellationToken)
    {
        await collection.InsertOneAsync(
            FileArtifactEnvelopeDocument.FromDomain(metadata.ToEnvelope()),
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyCollection<FileArtifactMetadata>> ListAsync(CancellationToken cancellationToken)
    {
        var documents = await collection
            .Find(FilterDefinition<FileArtifactEnvelopeDocument>.Empty)
            .SortByDescending(document => document.TimestampUtc)
            .ToListAsync(cancellationToken);

        return documents.Select(document => document.ToDomain()).ToList();
    }

    public async Task<FileArtifactMetadata?> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken)
    {
        var document = await collection
            .Find(item => item.DocumentId == documentId)
            .FirstOrDefaultAsync(cancellationToken);

        return document?.ToDomain();
    }

    private sealed class FileArtifactEnvelopeDocument
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("document_id")]
        public string DocumentId { get; set; } = string.Empty;

        [BsonElement("record_type")]
        public string RecordType { get; set; } = string.Empty;

        [BsonElement("source_type")]
        public string SourceType { get; set; } = string.Empty;

        [BsonElement("source_id")]
        public string SourceId { get; set; } = string.Empty;

        [BsonElement("timestamp_utc")]
        public DateTime TimestampUtc { get; set; }

        [BsonElement("ingested_at_utc")]
        public DateTime IngestedAtUtc { get; set; }

        [BsonElement("payload")]
        public FileArtifactPayloadDocument Payload { get; set; } = new();

        public static FileArtifactEnvelopeDocument FromDomain(PersistenceEnvelope<FileArtifactPayload> envelope)
        {
            return new FileArtifactEnvelopeDocument
            {
                DocumentId = envelope.Payload.DocumentId,
                RecordType = envelope.RecordType,
                SourceType = envelope.SourceType,
                SourceId = envelope.SourceId,
                TimestampUtc = envelope.TimestampUtc.UtcDateTime,
                IngestedAtUtc = envelope.IngestedAtUtc.UtcDateTime,
                Payload = FileArtifactPayloadDocument.FromDomain(envelope.Payload)
            };
        }

        public FileArtifactMetadata ToDomain()
        {
            return new FileArtifactMetadata(
                DocumentId,
                Payload.FileName,
                Payload.OriginalFileName,
                Payload.LocalPath,
                Payload.StoragePath,
                Payload.SizeBytes,
                Payload.UploadedBy,
                Payload.FileType,
                Payload.MimeType,
                Payload.Category,
                Payload.SourceSystem,
                new DateTimeOffset(TimestampUtc, TimeSpan.Zero));
        }
    }

    private sealed class FileArtifactPayloadDocument
    {
        [BsonElement("document_id")]
        public string DocumentId { get; set; } = string.Empty;

        [BsonElement("file_name")]
        public string FileName { get; set; } = string.Empty;

        [BsonElement("original_file_name")]
        public string OriginalFileName { get; set; } = string.Empty;

        [BsonElement("local_path")]
        public string LocalPath { get; set; } = string.Empty;

        [BsonElement("storage_type")]
        public string StorageType { get; set; } = string.Empty;

        [BsonElement("storage_path")]
        public string StoragePath { get; set; } = string.Empty;

        [BsonElement("size_bytes")]
        public long SizeBytes { get; set; }

        [BsonElement("uploaded_by")]
        public string UploadedBy { get; set; } = string.Empty;

        [BsonElement("file_type")]
        public string FileType { get; set; } = string.Empty;

        [BsonElement("mime_type")]
        public string MimeType { get; set; } = string.Empty;

        [BsonElement("category")]
        public string? Category { get; set; }

        [BsonElement("source_system")]
        public string SourceSystem { get; set; } = string.Empty;

        public static FileArtifactPayloadDocument FromDomain(FileArtifactPayload payload)
        {
            return new FileArtifactPayloadDocument
            {
                DocumentId = payload.DocumentId,
                FileName = payload.FileName,
                OriginalFileName = payload.OriginalFileName,
                LocalPath = payload.LocalPath,
                StorageType = payload.StorageType,
                StoragePath = payload.StoragePath,
                SizeBytes = payload.SizeBytes,
                UploadedBy = payload.UploadedBy,
                FileType = payload.FileType,
                MimeType = payload.MimeType,
                Category = payload.Category,
                SourceSystem = payload.SourceSystem
            };
        }
    }
}

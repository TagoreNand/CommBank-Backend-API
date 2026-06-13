using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CommBank.AI.Services;

/// <summary>
/// MongoDB-backed, append-only audit sink for automated model decisions. Only ever inserts (never
/// updates or deletes), so the trail is immutable by construction. Writes are best-effort: a failure
/// to record an audit row is logged but never propagated into the caller's financial flow.
/// </summary>
public sealed class MongoMlDecisionAuditService : IMlDecisionAuditService
{
    private readonly IMongoCollection<MlDecisionRecord> _collection;
    private readonly bool _enabled;
    private readonly ILogger<MongoMlDecisionAuditService> _logger;

    public MongoMlDecisionAuditService(
        IMongoDatabase database,
        IOptions<AiOptions> options,
        ILogger<MongoMlDecisionAuditService> logger)
    {
        AuditOptions audit = options.Value.Audit;
        _enabled = audit.Enabled;
        _logger = logger;
        _collection = database.GetCollection<MlDecisionRecord>(audit.CollectionName);

        // Best-effort index to accelerate idempotency lookups; never fatal if it can't be created.
        if (_enabled)
        {
            try
            {
                IndexKeysDefinition<MlDecisionRecord> keys = Builders<MlDecisionRecord>.IndexKeys
                    .Ascending(r => r.IdempotencyKey)
                    .Ascending(r => r.DecisionType);

                _collection.Indexes.CreateOne(
                    new CreateIndexModel<MlDecisionRecord>(keys, new CreateIndexOptions { Sparse = true, Name = "ix_idempotency_type" }));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not ensure ML audit index on {Collection}", audit.CollectionName);
            }
        }
    }

    public async Task RecordAsync(MlDecisionRecord record, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            record.Id = null; // let Mongo assign; guarantees insert (never an accidental overwrite)
            record.CreatedAtUtc = DateTime.UtcNow;
            await _collection.InsertOneAsync(record, options: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist ML decision record type={DecisionType} subject={SubjectId} user={UserId}",
                record.DecisionType, record.SubjectId, record.UserId);
        }
    }

    public async Task<MlDecisionRecord?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        string decisionType,
        CancellationToken cancellationToken = default)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        try
        {
            return await _collection
                .Find(r => r.IdempotencyKey == idempotencyKey && r.DecisionType == decisionType)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Idempotency lookup failed for key {IdempotencyKey} type {DecisionType}", idempotencyKey, decisionType);
            return null;
        }
    }
}

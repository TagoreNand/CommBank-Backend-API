using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Ledger;
using CommBank.Models;
using CommBank.Transfers.Abstractions;
using CommBank.Transfers.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CommBank.Transfers;

/// <summary>
/// ACID fund-transfer engine. Executes the debit, credit, both ledger transactions and the audit record
/// inside a single MongoDB multi-document transaction (snapshot read / majority write). Concurrency is
/// guarded optimistically via <see cref="Account.Version"/>; conflicts and transient transaction errors
/// are retried with a bounded budget; commits are retried on an unknown commit result. The whole op is
/// idempotent on the supplied key and may be gated by the fraud/AML risk model before commit.
/// </summary>
public sealed class FundTransferService : IFundTransferService
{
    private readonly IMongoClient _client;
    private readonly IMongoCollection<Account> _accounts;
    private readonly IMongoCollection<Transaction> _transactions;
    private readonly IMongoCollection<FundTransfer> _transfers;
    private readonly IMongoCollection<TransferIdempotencyRecord> _idempotency;
    private readonly IRiskScoringService _riskScorer;
    private readonly IMlDecisionAuditService _audit;
    private readonly ILedgerService _ledger;
    private readonly IOutboxService _outbox;
    private readonly TransferOptions _options;
    private readonly ILogger<FundTransferService> _logger;

    public FundTransferService(
        IMongoClient client,
        IMongoDatabase database,
        IRiskScoringService riskScorer,
        IMlDecisionAuditService audit,
        ILedgerService ledger,
        IOutboxService outbox,
        IOptions<TransferOptions> options,
        ILogger<FundTransferService> logger)
    {
        _client = client;
        _options = options.Value;
        _accounts = database.GetCollection<Account>("Accounts");
        _transactions = database.GetCollection<Transaction>("Transactions");
        _transfers = database.GetCollection<FundTransfer>(_options.TransfersCollection);
        _idempotency = database.GetCollection<TransferIdempotencyRecord>(_options.IdempotencyCollection);
        _riskScorer = riskScorer;
        _audit = audit;
        _ledger = ledger;
        _outbox = outbox;
        _logger = logger;

        TryEnsureIndexes();
    }

    public async Task<TransferResult> TransferAsync(
        TransferRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        TransferValidator.Validate(request);

        // Fast path: an already-completed idempotency key replays without opening a transaction.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            TransferResult? replay = await TryReplayAsync(idempotencyKey!, cancellationToken);
            if (replay is not null)
            {
                return replay;
            }
        }

        int maxAttempts = Math.Max(1, _options.MaxConcurrencyRetries);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await ExecuteAttemptAsync(request, idempotencyKey, cancellationToken);
            }
            catch (ConcurrencyConflictException) when (attempt < maxAttempts)
            {
                _logger.LogWarning("Transfer concurrency conflict (attempt {Attempt}/{Max}); retrying.", attempt, maxAttempts);
            }
            catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError") && attempt < maxAttempts)
            {
                _logger.LogWarning(ex, "Transient transaction error (attempt {Attempt}/{Max}); retrying.", attempt, maxAttempts);
            }
        }
    }

    private async Task<TransferResult> ExecuteAttemptAsync(
        TransferRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        using IClientSessionHandle session = await _client.StartSessionAsync(cancellationToken: ct);
        session.StartTransaction(new TransactionOptions(
            readConcern: ReadConcern.Snapshot,
            writeConcern: WriteConcern.WMajority,
            readPreference: ReadPreference.Primary));

        try
        {
            Account? source = await _accounts.Find(session, a => a.Id == request.SourceAccountId).FirstOrDefaultAsync(ct);
            Account? destination = await _accounts.Find(session, a => a.Id == request.DestinationAccountId).FirstOrDefaultAsync(ct);

            if (source is null)
            {
                throw new AccountNotFoundException(request.SourceAccountId);
            }

            if (destination is null)
            {
                throw new AccountNotFoundException(request.DestinationAccountId);
            }

            RiskAssessment? assessment = null;
            if (_options.RiskCheckEnabled)
            {
                assessment = await ScoreAsync(request, source, session, ct);
                if (assessment.Decision == RiskDecision.Block)
                {
                    // Audit is a separate, non-transactional write so it survives the abort.
                    await RecordRiskDecisionAsync(request, idempotencyKey, assessment, ct);
                    throw new TransferBlockedException(assessment);
                }
            }

            decimal sourceBalance = (decimal)source.Balance;
            if (!_options.AllowOverdraft && sourceBalance < request.Amount)
            {
                throw new InsufficientFundsException(
                    $"Source balance {sourceBalance:0.00} is insufficient for a transfer of {request.Amount:0.00}.");
            }

            decimal newSource = sourceBalance - request.Amount;
            decimal newDestination = (decimal)destination.Balance + request.Amount;

            // Conditional, version-checked updates. A zero ModifiedCount means a concurrent writer won.
            await ApplyBalanceAsync(session, source.Id!, source.Version, newSource, ct);
            await ApplyBalanceAsync(session, destination.Id!, destination.Version, newDestination, ct);

            DateTime now = DateTime.UtcNow;

            var debit = new Transaction
            {
                TransactionType = TransactionType.Transfer,
                Amount = (double)request.Amount,
                DateTime = now,
                UserId = request.UserId,
                Description = request.Description ?? $"Transfer to {request.DestinationAccountId}"
            };
            var credit = new Transaction
            {
                TransactionType = TransactionType.Credit,
                Amount = (double)request.Amount,
                DateTime = now,
                UserId = request.UserId,
                Description = request.Description ?? $"Transfer from {request.SourceAccountId}"
            };

            await _transactions.InsertOneAsync(session, debit, cancellationToken: ct);
            await _transactions.InsertOneAsync(session, credit, cancellationToken: ct);

            var transfer = new FundTransfer
            {
                SourceAccountId = request.SourceAccountId,
                DestinationAccountId = request.DestinationAccountId,
                Amount = request.Amount,
                Description = request.Description,
                Status = nameof(TransferStatus.Completed),
                IdempotencyKey = idempotencyKey,
                DebitTransactionId = debit.Id,
                CreditTransactionId = credit.Id,
                UserId = request.UserId,
                RiskScore = assessment?.Score,
                RiskDecision = assessment?.Decision.ToString(),
                CreatedAtUtc = now,
                CompletedAtUtc = now
            };

            await _transfers.InsertOneAsync(session, transfer, cancellationToken: ct);

            // Double-entry postings and the outbox event are written in the SAME transaction as the
            // balance changes: the ledger can never diverge from the money, and the event is published
            // if and only if the transfer commits.
            await _ledger.PostTransferAsync(session, transfer, ct);
            await _outbox.EnqueueAsync(session, "transfer.completed", new
            {
                transferId = transfer.Id,
                sourceAccountId = transfer.SourceAccountId,
                destinationAccountId = transfer.DestinationAccountId,
                amount = transfer.Amount,
                userId = transfer.UserId,
                completedAtUtc = transfer.CompletedAtUtc
            }, ct);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                try
                {
                    await _idempotency.InsertOneAsync(session, new TransferIdempotencyRecord
                    {
                        Id = idempotencyKey!,
                        TransferId = transfer.Id,
                        Status = nameof(TransferStatus.Completed),
                        CreatedAtUtc = now
                    }, cancellationToken: ct);
                }
                catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                {
                    // A concurrent request already owns this key: abandon ours, replay theirs.
                    await SafeAbortAsync(session, ct);
                    TransferResult? replay = await TryReplayAsync(idempotencyKey!, ct);
                    return replay ?? throw new ConcurrencyConflictException(
                        "A transfer with the supplied idempotency key is already in progress.");
                }
            }

            await CommitWithRetryAsync(session, ct);

            _logger.LogInformation(
                "Transfer {TransferId} completed: {Amount} from {Source} to {Destination}",
                transfer.Id, request.Amount, request.SourceAccountId, request.DestinationAccountId);

            return new TransferResult
            {
                TransferId = transfer.Id,
                Status = transfer.Status,
                Amount = request.Amount,
                SourceAccountId = request.SourceAccountId,
                DestinationAccountId = request.DestinationAccountId,
                SourceBalanceAfter = newSource,
                DestinationBalanceAfter = newDestination,
                DebitTransactionId = debit.Id,
                CreditTransactionId = credit.Id,
                Idempotent = false,
                RiskScore = assessment?.Score,
                RiskDecision = assessment?.Decision.ToString(),
                CompletedAtUtc = now
            };
        }
        catch (Exception)
        {
            await SafeAbortAsync(session, ct);
            throw;
        }
    }

    private async Task<RiskAssessment> ScoreAsync(
        TransferRequest request,
        Account source,
        IClientSessionHandle session,
        CancellationToken ct)
    {
        var recent = new List<Transaction>();
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            recent = await _transactions.Find(session, t => t.UserId == request.UserId).ToListAsync(ct);
        }

        var probe = new Transaction
        {
            TransactionType = TransactionType.Transfer,
            Amount = (double)request.Amount,
            DateTime = DateTime.UtcNow,
            UserId = request.UserId,
            Description = request.Description
        };

        var context = new RiskScoringContext
        {
            Account = source,
            RecentUserTransactions = recent,
            NowUtc = DateTime.UtcNow
        };

        return await _riskScorer.ScoreAsync(probe, context, ct);
    }

    private async Task ApplyBalanceAsync(
        IClientSessionHandle session,
        string accountId,
        long expectedVersion,
        decimal newBalance,
        CancellationToken ct)
    {
        UpdateDefinition<Account> update = Builders<Account>.Update
            .Set(a => a.Balance, (double)newBalance)
            .Inc(a => a.Version, 1L);

        UpdateResult result = await _accounts.UpdateOneAsync(session, VersionedFilter(accountId, expectedVersion), update, cancellationToken: ct);

        if (result.ModifiedCount == 0)
        {
            throw new ConcurrencyConflictException($"Account '{accountId}' was modified concurrently.");
        }
    }

    private static FilterDefinition<Account> VersionedFilter(string accountId, long expectedVersion)
    {
        FilterDefinitionBuilder<Account> f = Builders<Account>.Filter;

        if (expectedVersion == 0L)
        {
            // Legacy documents may predate the Version field; treat a missing field as version 0.
            return f.And(
                f.Eq(a => a.Id, accountId),
                f.Or(f.Eq(a => a.Version, 0L), f.Not(f.Exists(a => a.Version))));
        }

        return f.And(f.Eq(a => a.Id, accountId), f.Eq(a => a.Version, expectedVersion));
    }

    private async Task CommitWithRetryAsync(IClientSessionHandle session, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await session.CommitTransactionAsync(ct);
                return;
            }
            catch (MongoException ex) when (ex.HasErrorLabel("UnknownTransactionCommitResult"))
            {
                _logger.LogWarning("Unknown transaction commit result; retrying commit.");
            }
        }
    }

    private async Task<TransferResult?> TryReplayAsync(string idempotencyKey, CancellationToken ct)
    {
        TransferIdempotencyRecord? record = await _idempotency
            .Find(r => r.Id == idempotencyKey)
            .FirstOrDefaultAsync(ct);

        if (record is null || record.TransferId is null || record.Status != nameof(TransferStatus.Completed))
        {
            return null;
        }

        FundTransfer? transfer = await _transfers.Find(t => t.Id == record.TransferId).FirstOrDefaultAsync(ct);
        if (transfer is null)
        {
            return null;
        }

        // Post-transfer balances are not stored on the audit record; replays report the ledger amounts only.
        return new TransferResult
        {
            TransferId = transfer.Id,
            Status = transfer.Status,
            Amount = transfer.Amount,
            SourceAccountId = transfer.SourceAccountId,
            DestinationAccountId = transfer.DestinationAccountId,
            DebitTransactionId = transfer.DebitTransactionId,
            CreditTransactionId = transfer.CreditTransactionId,
            Idempotent = true,
            RiskScore = transfer.RiskScore,
            RiskDecision = transfer.RiskDecision,
            CompletedAtUtc = transfer.CompletedAtUtc ?? transfer.CreatedAtUtc
        };
    }

    private async Task RecordRiskDecisionAsync(
        TransferRequest request,
        string? idempotencyKey,
        RiskAssessment assessment,
        CancellationToken ct)
    {
        var record = new MlDecisionRecord
        {
            DecisionType = "transfer-risk",
            ModelId = assessment.ModelId,
            ModelVersion = assessment.ModelVersion,
            UserId = request.UserId,
            IdempotencyKey = idempotencyKey,
            Score = assessment.Score,
            Decision = assessment.Decision.ToString(),
            ReasonsJson = System.Text.Json.JsonSerializer.Serialize(assessment.Reasons)
        };

        await _audit.RecordAsync(record, ct);
    }

    private static async Task SafeAbortAsync(IClientSessionHandle session, CancellationToken ct)
    {
        try
        {
            if (session.IsInTransaction)
            {
                await session.AbortTransactionAsync(ct);
            }
        }
        catch
        {
            // Best-effort rollback; the transaction will also abort on session disposal/timeout.
        }
    }

    private void TryEnsureIndexes()
    {
        try
        {
            IndexKeysDefinition<FundTransfer> keys = Builders<FundTransfer>.IndexKeys.Ascending(t => t.IdempotencyKey);
            _transfers.Indexes.CreateOne(new CreateIndexModel<FundTransfer>(
                keys, new CreateIndexOptions { Sparse = true, Name = "ix_transfer_idempotency" }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure transfer indexes.");
        }
    }
}

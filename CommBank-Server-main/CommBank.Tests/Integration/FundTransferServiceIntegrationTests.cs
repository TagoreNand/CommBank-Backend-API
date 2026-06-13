using CommBank.Ledger;
using CommBank.Ledger.Models;
using CommBank.Models;
using CommBank.Transfers;
using CommBank.Transfers.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CommBank.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="FundTransferService"/> against a real MongoDB replica set
/// (via Testcontainers). These exercise the actual multi-document transactions, so they prove the
/// atomicity, idempotency and optimistic-concurrency guarantees rather than asserting them in mocks.
/// Risk scoring is disabled here so the tests isolate transactional behaviour.
/// </summary>
[Collection("Mongo")]
public class FundTransferServiceIntegrationTests
{
    private readonly MongoReplicaSetFixture _fixture;

    public FundTransferServiceIntegrationTests(MongoReplicaSetFixture fixture) => _fixture = fixture;

    private IMongoCollection<Account> Accounts => _fixture.Database.GetCollection<Account>("Accounts");
    private IMongoCollection<Transaction> Transactions => _fixture.Database.GetCollection<Transaction>("Transactions");
    private IMongoCollection<FundTransfer> Transfers => _fixture.Database.GetCollection<FundTransfer>("Transfers");
    private IMongoCollection<LedgerEntry> LedgerEntries => _fixture.Database.GetCollection<LedgerEntry>("LedgerEntries");
    private IMongoCollection<OutboxMessage> Outbox => _fixture.Database.GetCollection<OutboxMessage>("Outbox");

    private FundTransferService NewService(int maxRetries = 25)
    {
        IOptions<TransferOptions> options = Options.Create(new TransferOptions
        {
            RiskCheckEnabled = false,
            MaxConcurrencyRetries = maxRetries
        });

        return new FundTransferService(
            _fixture.Client,
            _fixture.Database,
            new FakeApproveRiskScoringService(),
            new FakeMlDecisionAuditService(),
            new LedgerService(_fixture.Database),
            new OutboxService(_fixture.Database),
            options,
            NullLogger<FundTransferService>.Instance);
    }

    private async Task ResetAsync()
    {
        await _fixture.Database.DropCollectionAsync("Accounts");
        await _fixture.Database.DropCollectionAsync("Transactions");
        await _fixture.Database.DropCollectionAsync("Transfers");
        await _fixture.Database.DropCollectionAsync("TransferIdempotency");
        await _fixture.Database.DropCollectionAsync("LedgerEntries");
        await _fixture.Database.DropCollectionAsync("Outbox");
    }

    private async Task<Account> SeedAccountAsync(double balance)
    {
        var account = new Account
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = "test-account",
            Balance = balance,
            Version = 0
        };
        await Accounts.InsertOneAsync(account);
        return account;
    }

    private Task<Account> GetAccountAsync(string id) => Accounts.Find(a => a.Id == id).FirstAsync();

    [Fact]
    public async Task Transfer_WritesBalancedLedgerAndOutbox()
    {
        await ResetAsync();
        Account source = await SeedAccountAsync(1000d);
        Account destination = await SeedAccountAsync(0d);

        TransferResult result = await NewService().TransferAsync(
            new TransferRequest { SourceAccountId = source.Id!, DestinationAccountId = destination.Id!, Amount = 320m },
            idempotencyKey: null);

        // Double-entry: exactly two postings, equal amounts, opposite directions, one journal.
        List<LedgerEntry> entries = await LedgerEntries.Find(e => e.TransferId == result.TransferId).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Single(entries, e => e.Direction == LedgerDirection.Debit);
        Assert.Single(entries, e => e.Direction == LedgerDirection.Credit);
        Assert.Single(entries.Select(e => e.JournalId).Distinct());

        decimal debits = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
        decimal credits = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);
        Assert.Equal(debits, credits);

        // Outbox: one pending event written in the same transaction.
        List<OutboxMessage> outbox = await Outbox.Find(FilterDefinition<OutboxMessage>.Empty).ToListAsync();
        Assert.Single(outbox);
        Assert.Equal("transfer.completed", outbox[0].Type);
    }

    [Fact]
    public async Task Transfer_MovesFundsAtomically()
    {
        await ResetAsync();
        Account source = await SeedAccountAsync(1000d);
        Account destination = await SeedAccountAsync(500d);

        TransferResult result = await NewService().TransferAsync(
            new TransferRequest { SourceAccountId = source.Id!, DestinationAccountId = destination.Id!, Amount = 200m },
            idempotencyKey: null);

        Assert.Equal(nameof(TransferStatus.Completed), result.Status);

        Account sourceAfter = await GetAccountAsync(source.Id!);
        Account destinationAfter = await GetAccountAsync(destination.Id!);

        Assert.Equal(800d, sourceAfter.Balance);
        Assert.Equal(700d, destinationAfter.Balance);
        Assert.Equal(1L, sourceAfter.Version);
        Assert.Equal(2L, await Transactions.CountDocumentsAsync(FilterDefinition<Transaction>.Empty));
        Assert.Equal(1L, await Transfers.CountDocumentsAsync(FilterDefinition<FundTransfer>.Empty));
    }

    [Fact]
    public async Task Transfer_IsIdempotent_OnSameKey()
    {
        await ResetAsync();
        Account source = await SeedAccountAsync(1000d);
        Account destination = await SeedAccountAsync(0d);
        FundTransferService service = NewService();
        var request = new TransferRequest { SourceAccountId = source.Id!, DestinationAccountId = destination.Id!, Amount = 250m };
        string key = Guid.NewGuid().ToString();

        TransferResult first = await service.TransferAsync(request, key);
        TransferResult second = await service.TransferAsync(request, key);

        Assert.False(first.Idempotent);
        Assert.True(second.Idempotent);

        Account sourceAfter = await GetAccountAsync(source.Id!);
        Assert.Equal(750d, sourceAfter.Balance); // debited exactly once despite two calls
        Assert.Equal(1L, await Transfers.CountDocumentsAsync(FilterDefinition<FundTransfer>.Empty));
    }

    [Fact]
    public async Task Transfer_InsufficientFunds_Throws_AndLeavesBalancesUntouched()
    {
        await ResetAsync();
        Account source = await SeedAccountAsync(100d);
        Account destination = await SeedAccountAsync(0d);

        await Assert.ThrowsAsync<InsufficientFundsException>(() =>
            NewService().TransferAsync(
                new TransferRequest { SourceAccountId = source.Id!, DestinationAccountId = destination.Id!, Amount = 500m },
                idempotencyKey: null));

        Account sourceAfter = await GetAccountAsync(source.Id!);
        Account destinationAfter = await GetAccountAsync(destination.Id!);
        Assert.Equal(100d, sourceAfter.Balance);
        Assert.Equal(0d, destinationAfter.Balance);
        Assert.Equal(0L, await Transfers.CountDocumentsAsync(FilterDefinition<FundTransfer>.Empty));
    }

    [Fact]
    public async Task ConcurrentTransfers_HaveNoLostUpdates()
    {
        await ResetAsync();
        Account source = await SeedAccountAsync(1000d);
        Account destination = await SeedAccountAsync(0d);
        FundTransferService service = NewService(maxRetries: 50);

        const int count = 10;
        const decimal amount = 10m;

        Task<TransferResult>[] tasks = Enumerable.Range(0, count)
            .Select(_ => service.TransferAsync(
                new TransferRequest { SourceAccountId = source.Id!, DestinationAccountId = destination.Id!, Amount = amount },
                idempotencyKey: null))
            .ToArray();

        TransferResult[] results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(nameof(TransferStatus.Completed), r.Status));

        Account sourceAfter = await GetAccountAsync(source.Id!);
        Account destinationAfter = await GetAccountAsync(destination.Id!);

        // Decisive: optimistic concurrency means each debit landed exactly once — no lost updates.
        Assert.Equal(1000d - (count * (double)amount), sourceAfter.Balance);
        Assert.Equal(count * (double)amount, destinationAfter.Balance);
        Assert.Equal((long)count, sourceAfter.Version);
        Assert.Equal((long)count, await Transfers.CountDocumentsAsync(FilterDefinition<FundTransfer>.Empty));
    }
}

using CommBank.AI.Models;
using CommBank.AI.Services;
using CommBank.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CommBank.Tests.AI;

public class HeuristicRiskScoringServiceTests
{
    private static HeuristicRiskScoringService NewService(AiOptions? options = null) =>
        new(Options.Create(options ?? new AiOptions()), NullLogger<HeuristicRiskScoringService>.Instance);

    private static Transaction Debit(string id, double amount, DateTime when) => new()
    {
        Id = id,
        Amount = amount,
        TransactionType = TransactionType.Debit,
        DateTime = when,
        UserId = "u1"
    };

    [Fact]
    public async Task SmallNormalTransaction_IsApprovedLow()
    {
        var now = new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);
        var history = new List<Transaction>
        {
            Debit("1", 100, now.AddDays(-1)),
            Debit("2", 100, now.AddDays(-2)),
            Debit("3", 100, now.AddDays(-3)),
            Debit("4", 100, now.AddDays(-4))
        };
        var transaction = Debit("X", 105, now);
        var context = new RiskScoringContext { RecentUserTransactions = history, NowUtc = now };

        RiskAssessment assessment = await NewService().ScoreAsync(transaction, context);

        Assert.Equal(RiskDecision.Approve, assessment.Decision);
        Assert.Equal(RiskBand.Low, assessment.Band);
        Assert.False(assessment.Flagged);
    }

    [Fact]
    public async Task LargeAnomalousBurstTransfer_IsBlocked()
    {
        var now = new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);
        var history = new List<Transaction>
        {
            Debit("1", 100, now.AddMinutes(-1)),
            Debit("2", 120, now.AddMinutes(-2)),
            Debit("3", 90, now.AddMinutes(-3)),
            Debit("4", 110, now.AddMinutes(-4)),
            Debit("5", 80, now.AddMinutes(-5))
        };
        var transaction = new Transaction
        {
            Id = "X",
            Amount = 75000,
            TransactionType = TransactionType.Transfer,
            DateTime = now,
            UserId = "u1"
        };
        var context = new RiskScoringContext { RecentUserTransactions = history, NowUtc = now };

        RiskAssessment assessment = await NewService().ScoreAsync(transaction, context);

        Assert.True(assessment.Flagged);
        Assert.Equal(RiskDecision.Block, assessment.Decision);
        Assert.Contains(assessment.Reasons, r => r.Code == "AMOUNT_CRITICAL");
        Assert.Contains(assessment.Reasons, r => r.Code == "VELOCITY");
    }

    [Fact]
    public async Task DuplicateWithinTwoMinutes_RaisesRapidRepeat()
    {
        var now = new DateTime(2026, 6, 12, 12, 0, 0, DateTimeKind.Utc);
        var history = new List<Transaction> { Debit("1", 500, now.AddMinutes(-1)) };
        var transaction = Debit("X", 500, now);
        var context = new RiskScoringContext { RecentUserTransactions = history, NowUtc = now };

        RiskAssessment assessment = await NewService().ScoreAsync(transaction, context);

        Assert.Contains(assessment.Reasons, r => r.Code == "RAPID_REPEAT");
    }

    [Fact]
    public async Task Scoring_IsDeterministic()
    {
        var now = new DateTime(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);
        var history = new List<Transaction>
        {
            Debit("1", 200, now.AddDays(-1)),
            Debit("2", 300, now.AddDays(-2)),
            Debit("3", 250, now.AddDays(-3))
        };
        var transaction = Debit("X", 9500, now);
        var context = new RiskScoringContext { RecentUserTransactions = history, NowUtc = now };
        HeuristicRiskScoringService service = NewService();

        RiskAssessment first = await service.ScoreAsync(transaction, context);
        RiskAssessment second = await service.ScoreAsync(transaction, context);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Decision, second.Decision);
    }
}

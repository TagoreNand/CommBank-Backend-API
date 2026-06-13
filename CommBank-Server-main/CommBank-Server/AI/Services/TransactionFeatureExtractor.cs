using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CommBank.AI.Models;
using CommBank.Models;

namespace CommBank.AI.Services;

/// <summary>The deterministic feature vector derived from a transaction and its context.</summary>
internal sealed record TransactionFeatures
{
    public double Amount { get; init; }
    public TransactionType Type { get; init; }
    public double AmountZScore { get; init; }
    public int VelocityCount { get; init; }
    public double BalanceDrainRatio { get; init; }
    public bool IsOddHour { get; init; }
    public bool IsStructuringPattern { get; init; }
    public bool IsRoundLargeAmount { get; init; }
    public bool IsRapidRepeat { get; init; }
    public bool IsLargeTransfer { get; init; }
}

/// <summary>
/// Pure, side-effect-free feature engineering for risk scoring. Isolated from the scorer so the same
/// features can later feed an ML.NET model. Also produces a stable hash of the feature vector for the
/// audit trail (so inputs are provable without persisting raw PII).
/// </summary>
internal static class TransactionFeatureExtractor
{
    public static TransactionFeatures Extract(Transaction transaction, RiskScoringContext context, RiskScoringOptions options)
    {
        double amount = Math.Abs(transaction.Amount);
        IReadOnlyList<Transaction> history = context.RecentUserTransactions ?? Array.Empty<Transaction>();

        // Personal-history amount distribution, excluding the subject transaction itself.
        var amounts = history
            .Where(t => t.Id is null || t.Id != transaction.Id)
            .Select(t => Math.Abs(t.Amount))
            .ToList();

        double zScore = 0d;
        if (amounts.Count >= 3)
        {
            double mean = amounts.Average();
            double variance = amounts.Sum(a => (a - mean) * (a - mean)) / amounts.Count;
            double stdDev = Math.Sqrt(variance);
            if (stdDev > 0.0001d)
            {
                zScore = (amount - mean) / stdDev;
            }
        }
        zScore = Math.Max(0d, zScore); // only spending *above* the norm is risk-relevant

        DateTime windowStart = context.NowUtc.AddMinutes(-options.VelocityWindowMinutes);
        int velocityCount = history.Count(t =>
        {
            DateTime ts = AsUtc(t.DateTime);
            return ts >= windowStart && ts <= context.NowUtc;
        });

        double drainRatio = 0d;
        bool outflow = transaction.TransactionType is TransactionType.Debit or TransactionType.Transfer;
        if (outflow && context.Account is { Balance: > 0d })
        {
            drainRatio = amount / context.Account.Balance;
        }

        int hour = transaction.DateTime.Hour;
        bool oddHour = hour >= options.OddHourStartInclusive && hour < options.OddHourEndExclusive;

        // Smurfing/structuring: value parked just below a reporting threshold.
        bool structuring = amount >= options.HighAmountThreshold * 0.9d && amount < options.HighAmountThreshold;
        bool roundLarge = amount >= options.HighAmountThreshold && amount % 1000d == 0d;

        DateTime rapidWindowStart = context.NowUtc.AddMinutes(-2);
        bool rapidRepeat = history.Any(t =>
            (t.Id is null || t.Id != transaction.Id) &&
            t.TransactionType == transaction.TransactionType &&
            Math.Abs(Math.Abs(t.Amount) - amount) < 0.005d &&
            AsUtc(t.DateTime) >= rapidWindowStart &&
            AsUtc(t.DateTime) <= context.NowUtc);

        bool largeTransfer = transaction.TransactionType == TransactionType.Transfer && amount >= options.HighAmountThreshold;

        return new TransactionFeatures
        {
            Amount = amount,
            Type = transaction.TransactionType,
            AmountZScore = zScore,
            VelocityCount = velocityCount,
            BalanceDrainRatio = drainRatio,
            IsOddHour = oddHour,
            IsStructuringPattern = structuring,
            IsRoundLargeAmount = roundLarge,
            IsRapidRepeat = rapidRepeat,
            IsLargeTransfer = largeTransfer
        };
    }

    /// <summary>SHA-256 over the canonical feature vector. Stored in the audit trail instead of raw inputs.</summary>
    public static string ComputeHash(TransactionFeatures f)
    {
        string canonical = string.Join('|', new[]
        {
            f.Amount.ToString("F2", CultureInfo.InvariantCulture),
            ((int)f.Type).ToString(CultureInfo.InvariantCulture),
            f.AmountZScore.ToString("F4", CultureInfo.InvariantCulture),
            f.VelocityCount.ToString(CultureInfo.InvariantCulture),
            f.BalanceDrainRatio.ToString("F4", CultureInfo.InvariantCulture),
            f.IsOddHour ? "1" : "0",
            f.IsStructuringPattern ? "1" : "0",
            f.IsRoundLargeAmount ? "1" : "0",
            f.IsRapidRepeat ? "1" : "0",
            f.IsLargeTransfer ? "1" : "0"
        });

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    // Transaction timestamps are persisted as wall-clock values; treat unspecified/local as the same
    // frame as the evaluation clock so window comparisons are stable and tz-bug-free.
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

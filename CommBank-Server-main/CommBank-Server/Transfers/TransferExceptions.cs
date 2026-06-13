using CommBank.AI.Models;

namespace CommBank.Transfers;

/// <summary>Base type for all transfer-domain failures, mapped to HTTP problem responses at the edge.</summary>
public abstract class TransferException : Exception
{
    protected TransferException(string message) : base(message) { }
}

/// <summary>Request is structurally invalid (e.g. non-positive amount, same source and destination).</summary>
public sealed class InvalidTransferException : TransferException
{
    public InvalidTransferException(string message) : base(message) { }
}

/// <summary>A referenced account does not exist.</summary>
public sealed class AccountNotFoundException : TransferException
{
    public string AccountId { get; }

    public AccountNotFoundException(string accountId)
        : base($"Account '{accountId}' was not found.") => AccountId = accountId;
}

/// <summary>The source account has insufficient available balance and overdraft is disabled.</summary>
public sealed class InsufficientFundsException : TransferException
{
    public InsufficientFundsException(string message) : base(message) { }
}

/// <summary>A concurrent modification was detected and retries were exhausted.</summary>
public sealed class ConcurrencyConflictException : TransferException
{
    public ConcurrencyConflictException(string message) : base(message) { }
}

/// <summary>The transfer was rejected by the fraud/AML risk policy.</summary>
public sealed class TransferBlockedException : TransferException
{
    public RiskAssessment Assessment { get; }

    public TransferBlockedException(RiskAssessment assessment)
        : base("Transfer blocked by risk policy.") => Assessment = assessment;
}

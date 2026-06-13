using System.ComponentModel.DataAnnotations;

namespace CommBank.Transfers.Models;

/// <summary>
/// Inbound request to move funds between two accounts. Amount is <see cref="decimal"/> — money is never
/// represented as a binary float. Combined with the <c>Idempotency-Key</c> header, a retried request is
/// guaranteed to execute at most once.
/// </summary>
public sealed class TransferRequest
{
    [Required]
    public string SourceAccountId { get; set; } = "";

    [Required]
    public string DestinationAccountId { get; set; } = "";

    [Range(typeof(decimal), "0.01", "100000000", ErrorMessage = "Amount must be between 0.01 and 100,000,000.")]
    public decimal Amount { get; set; }

    [StringLength(200)]
    public string? Description { get; set; }

    /// <summary>The user initiating the transfer; flows into risk scoring and the audit trail.</summary>
    public string? UserId { get; set; }
}

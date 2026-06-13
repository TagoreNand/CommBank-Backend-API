using CommBank.Transfers.Models;

namespace CommBank.Transfers;

/// <summary>Pure, side-effect-free request validation. Extracted so it is exhaustively unit-testable.</summary>
public static class TransferValidator
{
    public static void Validate(TransferRequest request)
    {
        if (request is null)
        {
            throw new InvalidTransferException("A transfer request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceAccountId))
        {
            throw new InvalidTransferException("Source account id is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationAccountId))
        {
            throw new InvalidTransferException("Destination account id is required.");
        }

        if (string.Equals(request.SourceAccountId, request.DestinationAccountId, StringComparison.Ordinal))
        {
            throw new InvalidTransferException("Source and destination accounts must be different.");
        }

        if (request.Amount <= 0m)
        {
            throw new InvalidTransferException("Transfer amount must be greater than zero.");
        }

        if (decimal.Round(request.Amount, 2) != request.Amount)
        {
            throw new InvalidTransferException("Transfer amount cannot have more than two decimal places.");
        }
    }
}

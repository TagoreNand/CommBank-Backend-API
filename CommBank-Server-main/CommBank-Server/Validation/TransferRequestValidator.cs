using CommBank.Transfers.Models;
using FluentValidation;

namespace CommBank.Validation;

public sealed class TransferRequestValidator : AbstractValidator<TransferRequest>
{
    public TransferRequestValidator()
    {
        RuleFor(x => x.SourceAccountId).NotEmpty().Length(24).WithMessage("A 24-character account id is required.");
        RuleFor(x => x.DestinationAccountId)
            .NotEmpty().Length(24).WithMessage("A 24-character account id is required.")
            .NotEqual(x => x.SourceAccountId).WithMessage("Source and destination accounts must differ.");
        RuleFor(x => x.Amount)
            .GreaterThan(0m)
            .Must(amount => decimal.Round(amount, 2) == amount).WithMessage("Amount cannot have more than two decimal places.");
        RuleFor(x => x.Description).MaximumLength(200);
    }
}

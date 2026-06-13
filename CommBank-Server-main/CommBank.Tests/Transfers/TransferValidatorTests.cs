using CommBank.Transfers;
using CommBank.Transfers.Models;

namespace CommBank.Tests.Transfers;

public class TransferValidatorTests
{
    private static TransferRequest Valid() => new()
    {
        SourceAccountId = "aaaaaaaaaaaaaaaaaaaaaaaa",
        DestinationAccountId = "bbbbbbbbbbbbbbbbbbbbbbbb",
        Amount = 100.50m
    };

    [Fact]
    public void ValidRequest_DoesNotThrow()
    {
        TransferValidator.Validate(Valid());
    }

    [Fact]
    public void SameSourceAndDestination_Throws()
    {
        var request = Valid();
        request.DestinationAccountId = request.SourceAccountId;

        Assert.Throws<InvalidTransferException>(() => TransferValidator.Validate(request));
    }

    [Fact]
    public void NonPositiveAmount_Throws()
    {
        var request = Valid();
        request.Amount = 0m;

        Assert.Throws<InvalidTransferException>(() => TransferValidator.Validate(request));
    }

    [Fact]
    public void MoreThanTwoDecimalPlaces_Throws()
    {
        var request = Valid();
        request.Amount = 10.125m;

        Assert.Throws<InvalidTransferException>(() => TransferValidator.Validate(request));
    }

    [Fact]
    public void MissingSourceAccount_Throws()
    {
        var request = Valid();
        request.SourceAccountId = "";

        Assert.Throws<InvalidTransferException>(() => TransferValidator.Validate(request));
    }
}

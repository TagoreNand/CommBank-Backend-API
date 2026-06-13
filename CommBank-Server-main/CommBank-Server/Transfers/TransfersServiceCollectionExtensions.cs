using CommBank.Transfers.Abstractions;
using CommBank.Transfers.Models;

namespace CommBank.Transfers;

/// <summary>Composition root for the fund-transfer module.</summary>
public static class TransfersServiceCollectionExtensions
{
    public static IServiceCollection AddCommBankTransfers(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TransferOptions>(configuration.GetSection(TransferOptions.SectionName));
        services.AddSingleton<IFundTransferService, FundTransferService>();
        return services;
    }
}

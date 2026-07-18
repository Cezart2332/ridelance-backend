using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using SharedKernel;

namespace Application.Banking.Queries;

public sealed record BankInstitutionResponse(
    string Id,
    string Name,
    string? Logo,
    int MaxHistoricalDays);

public sealed record GetBankInstitutionsQuery : IQuery<List<BankInstitutionResponse>>;

internal sealed class GetBankInstitutionsQueryHandler(IBankDataProvider provider)
    : IQueryHandler<GetBankInstitutionsQuery, List<BankInstitutionResponse>>
{
    public async Task<Result<List<BankInstitutionResponse>>> Handle(
        GetBankInstitutionsQuery query,
        CancellationToken cancellationToken)
    {
        if (!provider.IsConfigured)
        {
            return Result.Failure<List<BankInstitutionResponse>>(Error.Problem(
                "Bank.NotConfigured",
                "Conectarea băncii nu este disponibilă momentan. Poți încărca extrasul de cont manual, din Documente."));
        }

        try
        {
            IReadOnlyList<BankInstitutionInfo> institutions =
                await provider.ListInstitutionsAsync("ro", cancellationToken);

            return institutions
                .Select(i => new BankInstitutionResponse(i.Id, i.Name, i.Logo, i.MaxHistoricalDays))
                .ToList();
        }
        catch (BankDataProviderException ex)
        {
            return Result.Failure<List<BankInstitutionResponse>>(
                Error.Problem("Bank.ProviderError", ex.Message));
        }
    }
}

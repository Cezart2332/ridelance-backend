using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using SharedKernel;

namespace Application.Banking.Queries;

/// <param name="RequiresPsuId">Banca cere numele de utilizator de la ea înainte de acord.</param>
/// <param name="RequiresPsuIdType">Banca cere să spunem dacă e cont de persoană fizică sau de firmă.</param>
/// <param name="RequiresIban">Banca cere IBAN-ul contului pentru care se dă acordul.</param>
public sealed record BankInstitutionResponse(
    string Id,
    string Name,
    string? Logo,
    bool RequiresPsuId,
    bool RequiresPsuIdType,
    bool RequiresIban);

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
                await provider.ListInstitutionsAsync(cancellationToken);

            return institutions
                .Select(i => new BankInstitutionResponse(
                    i.Id, i.Name, i.Logo, i.RequiresPsuId, i.RequiresPsuIdType, i.RequiresIban))
                .ToList();
        }
        catch (BankDataProviderException ex)
        {
            return Result.Failure<List<BankInstitutionResponse>>(
                Error.Problem("Bank.ProviderError", ex.Message));
        }
    }
}

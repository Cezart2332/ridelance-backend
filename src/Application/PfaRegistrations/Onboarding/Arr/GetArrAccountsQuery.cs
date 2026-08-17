using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

/// <summary>
/// Un cont de trezorerie ARR, gata de afișat. IBAN-ul pleacă fără spații — gruparea în blocuri
/// de 4 e decizie de lizibilitate, deci a UI-ului, iar copierea trebuie să dea forma canonică.
/// </summary>
public sealed record ArrAccountResponse(
    string CountyCode,
    string CountyName,
    string BeneficiaryName,
    string Treasury,
    string FiscalCode,
    string Iban);

/// <summary>
/// Lista conturilor ARR active. Publică pentru orice utilizator autentificat: aceleași conturi
/// sunt afișate pe toate ramurile de onboarding (cu PFA / fără PFA / proprietate / leasing).
/// </summary>
public sealed record GetArrAccountsQuery : IQuery<IReadOnlyList<ArrAccountResponse>>;

internal sealed class GetArrAccountsQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetArrAccountsQuery, IReadOnlyList<ArrAccountResponse>>
{
    public async Task<Result<IReadOnlyList<ArrAccountResponse>>> Handle(
        GetArrAccountsQuery query,
        CancellationToken cancellationToken)
    {
        List<ArrAccount> accounts = await context.ArrAccounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.CountyName)
            .ToListAsync(cancellationToken);

        IReadOnlyList<ArrAccountResponse> response = accounts
            .Select(a => new ArrAccountResponse(
                a.CountyCode,
                a.CountyName,
                a.BeneficiaryName,
                a.Treasury,
                a.FiscalCode,
                a.Iban))
            .ToList();

        return Result.Success(response);
    }
}

using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>Adresele de sediu social puse la dispoziție de Consulto, în ordinea de afișare.</summary>
public sealed record GetConsultoOfficesQuery : IQuery<IReadOnlyList<ConsultoOfficeDto>>;

internal sealed class GetConsultoOfficesQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetConsultoOfficesQuery, IReadOnlyList<ConsultoOfficeDto>>
{
    public async Task<Result<IReadOnlyList<ConsultoOfficeDto>>> Handle(
        GetConsultoOfficesQuery query,
        CancellationToken cancellationToken)
    {
        // Adresa se compune în memorie: ToDisplayString sare peste părțile lipsă, ceea ce nu
        // se poate exprima în SQL fără un lanț de CASE-uri.
        List<ConsultoOffice> offices = await context.ConsultoOffices
            .AsNoTracking()
            .Where(o => o.IsActive)
            .OrderBy(o => o.Position)
            .ToListAsync(cancellationToken);

        IReadOnlyList<ConsultoOfficeDto> result = offices
            .Select(o => new ConsultoOfficeDto(o.Id, o.ToDisplayString(), o.MonthlyFeeBani, o.YearlyFeeBani))
            .ToList();

        return Result.Success(result);
    }
}

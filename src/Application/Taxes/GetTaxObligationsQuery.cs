using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.AiVerification;
using Domain.PfaRegistrations;
using Domain.Taxes;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Taxes;

/// <summary>
/// Obligațiile fiscale ale unui PFA. Fără <paramref name="PfaRegistrationId"/> se citesc ale
/// utilizatorului curent — cazul clientului; cu el, ale unui client anume, pentru contabilă.
/// </summary>
public sealed record GetTaxObligationsQuery(Guid? PfaRegistrationId, int? Year)
    : IQuery<List<TaxObligationResponse>>;

internal sealed class GetTaxObligationsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetTaxObligationsQuery, List<TaxObligationResponse>>
{
    public async Task<Result<List<TaxObligationResponse>>> Handle(
        GetTaxObligationsQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<List<TaxObligationResponse>>(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        PfaRegistration? pfa = query.PfaRegistrationId is null
            ? await context.PfaRegistrations
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
            : await context.PfaRegistrations
                .AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == query.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<List<TaxObligationResponse>>(
                Error.NotFound("PfaRegistration.NotFound", "Nu există un PFA pentru acest cont."));
        }

        bool canView = caller.Role is UserRole.Admin
            || pfa.UserId == userId
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userId;

        if (!canView)
        {
            return Result.Failure<List<TaxObligationResponse>>(
                Error.Failure("TaxObligation.Forbidden", "Nu ai acces la obligațiile acestui PFA."));
        }

        IQueryable<TaxObligation> obligations = context.TaxObligations
            .AsNoTracking()
            .Where(o => o.PfaRegistrationId == pfa.Id);

        if (query.Year.HasValue)
        {
            obligations = obligations.Where(o => o.PeriodYear == query.Year.Value);
        }

        List<TaxObligation> items = await obligations
            .OrderByDescending(o => o.PeriodYear)
            .ThenByDescending(o => o.PeriodMonth)
            .ToListAsync(cancellationToken);

        DateOnly today = DocumentDateValidator.TodayInRomania();

        return items.Select(o => TaxObligationMapper.ToResponse(o, today)).ToList();
    }
}

using Application.Abstractions.Data;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Application.Payments;

/// <summary>
/// Ce poate face un client după plan. Un singur loc, ca endpoint-urile și jobul de contabilitate să
/// nu ajungă la răspunsuri diferite.
///
/// PFAlone: își ține singur registrele (le vede și le completează) și are generatorul de declarații;
/// nu intră la contabil și nimic nu se completează automat în registrele lui.
/// PFA Full: contabilul se ocupă de tot; registrele nu sunt ale clientului de văzut.
/// </summary>
public static class PlanAccess
{
    /// <summary>Planul abonamentului activ al omului, sau null fără abonament activ.</summary>
    public static Task<SubscriptionPlan?> ActivePlanAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.UserSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.ActivePendingBilling))
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => (SubscriptionPlan?)s.Plan)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>PFAlone: registrele și declarațiile sunt treaba lui.</summary>
    public static async Task<bool> ManagesOwnBooksAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken) =>
        await ActivePlanAsync(db, userId, cancellationToken) == SubscriptionPlan.PfaAlone;

    /// <summary>
    /// Dosarele PFAlone, după id-ul dosarului PFA: ies din contabilitate (lotul lunar, lista
    /// contabilului) și din completarea automată a registrelor.
    /// </summary>
    public static IQueryable<Guid> SelfManagedPfaIds(IApplicationDbContext db) =>
        db.PfaRegistrations
            .Where(p => db.UserSubscriptions.Any(s =>
                s.UserId == p.UserId
                && s.Plan == SubscriptionPlan.PfaAlone
                && (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.ActivePendingBilling)))
            .Select(p => p.Id);
}

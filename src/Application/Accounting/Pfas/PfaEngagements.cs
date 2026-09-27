using System.Globalization;
using Application.Abstractions.Data;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Pfas;

/// <summary>
/// Colaborarea contabilă a unui PFA (<see cref="PfaAccountingEngagement"/>). Un PFA fără rând
/// explicit e activ de la încheierea onboarding-ului (aceeași regulă ca scope-ul lunii, B3).
/// </summary>
internal static class PfaEngagements
{
    public static async Task<PfaAccountingEngagement?> LatestAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken) =>
        await db.PfaAccountingEngagements
            .Where(e => e.PfaRegistrationId == pfaId)
            .OrderByDescending(e => e.Status == EngagementStatus.Active)
            .ThenByDescending(e => e.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

    public static async Task<EngagementInfo?> InfoAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken)
    {
        if (await LatestAsync(db, pfaId, cancellationToken) is { } engagement)
        {
            return new EngagementInfo(engagement.Status, engagement.StartDate, engagement.EndDate);
        }

        var pfa = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == pfaId)
            .Select(p => new { p.OnboardingCompletedAtUtc, p.CreatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        return pfa is null
            ? null
            : new EngagementInfo(EngagementStatus.Active, DateOnly.FromDateTime(pfa.OnboardingCompletedAtUtc ?? pfa.CreatedAtUtc), null);
    }

    /// <summary>
    /// Luna fiscală de lucru: luna trecută, cea pentru care se depun acum declarațiile (D100, D301 și
    /// D390 au scadența pe 25 a lunii următoare).
    /// </summary>
    public static string CurrentPeriod(DateOnly today) =>
        today.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

/// <summary>
/// Termenul minim de păstrare a documentelor (spec contabilitate B8): 1 iulie (configurabil) al
/// anului următor documentului + N ani − 1 zi; pentru 2026 și 5 ani → 30.06.2032. Nimic nu se șterge
/// automat, iar purge-ul nu există în V1.
/// </summary>
public static class RetentionService
{
    public static DateOnly MinimumRetentionUntil(int documentYear, RetentionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new DateOnly(documentYear + 1 + policy.YearsAfter, policy.StartMonth, policy.StartDay).AddDays(-1);
    }

    /// <summary>Politica valabilă la sfârșitul anului documentului; implicit 5 ani de la 1 iulie.</summary>
    public static async Task<DateOnly> MinimumRetentionUntilAsync(IApplicationDbContext db, int documentYear, CancellationToken cancellationToken)
    {
        var yearEnd = new DateOnly(documentYear, 12, 31);
        RetentionPolicy policy = await db.RetentionPolicies.AsNoTracking()
            .Where(p => p.ValidFrom <= yearEnd && (p.ValidTo == null || p.ValidTo >= yearEnd))
            .OrderByDescending(p => p.ValidFrom)
            .FirstOrDefaultAsync(cancellationToken)
            ?? new RetentionPolicy { YearsAfter = 5, StartMonth = 7, StartDay = 1 };
        return MinimumRetentionUntil(documentYear, policy);
    }
}

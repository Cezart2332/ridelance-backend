using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Months;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Pfas;

/// <summary><c>GET /accounting/pfas/{pfaId}/summary</c> — antetul dosarului PFA.</summary>
public sealed record GetPfaSummaryQuery(Guid PfaId) : IQuery<PfaAccountingSummary>;

internal sealed class GetPfaSummaryQueryHandler(IApplicationDbContext db) : IQueryHandler<GetPfaSummaryQuery, PfaAccountingSummary>
{
    public async Task<Result<PfaAccountingSummary>> Handle(GetPfaSummaryQuery query, CancellationToken cancellationToken)
    {
        PfaAccountingSummary? summary = await PfaSummaries.BuildAsync(db, query.PfaId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
        return summary is null ? Result.Failure<PfaAccountingSummary>(AccountingErrors.PfaNotFound) : summary;
    }
}

internal static class PfaSummaries
{
    public static async Task<PfaAccountingSummary?> BuildAsync(IApplicationDbContext db, Guid pfaId, DateOnly today, CancellationToken cancellationToken)
    {
        var pfa = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == pfaId)
            .Select(p => new { p.Id, p.LegalName, p.HolderName, p.FullName, p.Cui, p.UserId, p.User.FirstName, p.User.LastName, p.User.Email, p.User.PhoneNumber })
            .SingleOrDefaultAsync(cancellationToken);
        if (pfa is null)
        {
            return null;
        }

        EngagementInfo engagement = (await PfaEngagements.InfoAsync(db, pfaId, cancellationToken))!;
        string period = PfaEngagements.CurrentPeriod(today);
        MonthData data = await MonthData.LoadAsync(db, period, [pfaId], cancellationToken);
        Art317Period? art317 = data.Art317Of(pfaId).Where(p => p.ValidFrom <= today).OrderByDescending(p => p.ValidFrom).FirstOrDefault();
        PfaMonthStatus monthStatus = await db.PfaMonthChecks.AsNoTracking()
            .Where(c => c.PfaRegistrationId == pfaId && c.Period == period)
            .Select(c => (PfaMonthStatus?)c.Status)
            .FirstOrDefaultAsync(cancellationToken) ?? PfaMonthStatus.NotProcessed;

        bool inactive = engagement.Status == EngagementStatus.Inactive;
        DateOnly? retention = inactive && engagement.EndDate is { } end
            ? await RetentionService.MinimumRetentionUntilAsync(db, end.Year, cancellationToken)
            : null;

        return new PfaAccountingSummary(
            pfa.Id,
            PfaNames.Of(pfa.LegalName, pfa.HolderName, pfa.FullName, pfa.FirstName, pfa.LastName),
            pfa.Cui ?? string.Empty,
            // Sistem real, neplătitor de TVA: profilul PFA-urilor de ridesharing din spec (§1).
            RealSystem: true,
            VatPayer: false,
            Art317: art317 is { Enabled: true },
            Art317ActivationDate: art317 is { Enabled: true } ? art317.ValidFrom : null,
            Platforms: data.PlatformsOf(pfaId) ?? [],
            Engagement: engagement,
            CurrentPeriod: period,
            CurrentMonthStatus: monthStatus,
            Cash: await CashAsync(db, pfaId, cancellationToken),
            ReadOnly: inactive,
            RetentionUntil: retention,
            Client: new ClientContact(pfa.UserId, pfa.Email, pfa.PhoneNumber));
    }

    private static async Task<CashRegisterStateDto> CashAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken)
    {
        CashRegisterState? cash = await db.CashRegisterStates.AsNoTracking().FirstOrDefaultAsync(c => c.PfaRegistrationId == pfaId, cancellationToken);
        if (cash is null)
        {
            return new CashRegisterStateDto(CashRegisterStatus.NotRequiredCurrentConfiguration, false, false, null, null, null);
        }

        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, [cash.VerifiedByUserId], cancellationToken);
        var evidence = cash.EvidenceDocumentId is { } id
            ? await db.Documents.AsNoTracking().Where(d => d.Id == id).Select(d => new { d.Id, d.OriginalFileName, d.ContentType, d.FileSize }).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new CashRegisterStateDto(
            cash.Status,
            cash.CashRequested,
            cash.CashEnabled,
            cash.ActivationDate,
            cash.VerifiedByUserId is { } by && users.TryGetValue(by, out UserRef? user) ? user : null,
            evidence is null ? null : new StoredFileRef(evidence.Id, evidence.OriginalFileName, evidence.ContentType, evidence.FileSize, string.Empty));
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/deactivate</c> — <c>{ accountingEndDate }</c>.</summary>
public sealed record DeactivatePfaCommand(Guid PfaId, DateOnly AccountingEndDate) : ICommand<PfaAccountingSummary>;

/// <summary>
/// Inactivarea (spec contabilitate B8): colaborarea primește data de sfârșit și devine
/// <c>INACTIVE</c>; dosarul rămâne doar de consultat, iar importatorii nu mai aduc operațiuni de după
/// această dată. Datele nu se șterg: păstrarea minimă e afișată în sumar.
/// </summary>
internal sealed class DeactivatePfaCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<DeactivatePfaCommand, PfaAccountingSummary>
{
    public static readonly Error EndBeforeStart = Error.Problem("Accounting.InvalidEndDate", "Data de sfârșit e înainte de începutul colaborării.");

    public async Task<Result<PfaAccountingSummary>> Handle(DeactivatePfaCommand command, CancellationToken cancellationToken)
    {
        EngagementInfo? info = await PfaEngagements.InfoAsync(db, command.PfaId, cancellationToken);
        if (info is null)
        {
            return Result.Failure<PfaAccountingSummary>(AccountingErrors.PfaNotFound);
        }

        if (info.Status == EngagementStatus.Inactive)
        {
            return Result.Failure<PfaAccountingSummary>(AccountingErrors.PfaReadOnly);
        }

        if (command.AccountingEndDate < info.StartDate)
        {
            return Result.Failure<PfaAccountingSummary>(EndBeforeStart);
        }

        PfaAccountingEngagement engagement = await PfaEngagements.LatestAsync(db, command.PfaId, cancellationToken)
            ?? db.PfaAccountingEngagements.Add(new PfaAccountingEngagement
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaId,
                StartDate = info.StartDate,
                Status = EngagementStatus.Active,
            }).Entity;

        var before = new { engagement.Status, engagement.StartDate, engagement.EndDate };
        engagement.EndDate = command.AccountingEndDate;
        engagement.Status = EngagementStatus.Inactive;
        AccountingAudit.Record(
            db, command.PfaId, nameof(PfaAccountingEngagement), engagement.Id, "DEACTIVATE", before,
            new { engagement.Status, engagement.StartDate, engagement.EndDate }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        return (await PfaSummaries.BuildAsync(db, command.PfaId, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken))!;
    }
}

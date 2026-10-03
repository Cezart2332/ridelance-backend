using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Months;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary>Ce s-a schimbat față de versiunea acceptată de ANAF.</summary>
internal sealed record RectificationDiff(decimal SubmittedTotal, decimal CurrentTotal, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);

/// <summary>
/// Rectificările de făcut (spec declarații §6): o perioadă cu declarație acceptată se recalculează la
/// reprocesare; dacă rezultatul diferă de versiunea depusă, apare un task cu diff-ul, până la
/// rectificativă. Versiunea depusă nu se atinge.
/// </summary>
internal static class RectificationTasks
{
    public static async Task CheckAsync(IApplicationDbContext db, ScopePfa pfa, MonthData data, TaxEngineSettings settings, CancellationToken cancellationToken)
    {
        List<DeclarationVersion> accepted = await db.DeclarationVersions
            .AsNoTracking()
            .Include(v => v.Declaration)
            .Where(v => v.Declaration.PfaRegistrationId == pfa.Id && v.Declaration.Period == data.Period &&
                        v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo) &&
                        v.Status == DeclarationStatus.Accepted)
            .ToListAsync(cancellationToken);
        if (accepted.Count == 0)
        {
            return;
        }

        TaxResult current = MonthlyTaxEngine.Calculate(data.TaxInput(pfa.Id, settings));
        foreach (DeclarationVersion version in accepted)
        {
            if (DeclarationSnapshot.Read(version.SnapshotJson) is not { } snapshot ||
                await db.DeclarationRectificationTasks.AnyAsync(t => t.DeclarationId == version.DeclarationId && t.ResolvedAtUtc == null, cancellationToken))
            {
                continue;
            }

            DeclarationCalculation now = current.Declarations[version.Declaration.Type];
            HashSet<string> before = [.. snapshot.Calculation.Lines.Select(Key)];
            HashSet<string> after = [.. now.Lines.Select(Key)];
            if (before.SetEquals(after) && snapshot.Calculation.Total == now.Total)
            {
                continue;
            }

            db.DeclarationRectificationTasks.Add(new DeclarationRectificationTask
            {
                Id = Guid.NewGuid(),
                DeclarationId = version.DeclarationId,
                AcceptedVersionId = version.Id,
                DiffJson = AccountingJson.Serialize(new RectificationDiff(
                    snapshot.Calculation.Total, now.Total, [.. after.Except(before)], [.. before.Except(after)])),
                DetectedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Rectificativa rezolvă task-urile deschise ale declarației.</summary>
    public static async Task ResolveAsync(IApplicationDbContext db, Guid declarationId, Guid versionId, CancellationToken cancellationToken)
    {
        List<DeclarationRectificationTask> open = await db.DeclarationRectificationTasks
            .Where(t => t.DeclarationId == declarationId && t.ResolvedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (DeclarationRectificationTask task in open)
        {
            task.ResolvedByVersionId = versionId;
            task.ResolvedAtUtc = DateTime.UtcNow;
        }
    }

    private static string Key(TaxLine line) =>
        $"{line.SupplierName} {AccountingJson.Amount(line.Base)} × {line.Rate?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "-"}% = {AccountingJson.Amount(line.Value)}";
}

/// <summary>Un rând din „Necesită atenție” (spec declarații §6–§7).</summary>
/// <param name="Reason">Recipisă cu erori, validare eșuată, rectificare de făcut, D100 până la confirmarea regulii.</param>
public sealed record DeclarationAttentionDto(Guid PfaId, string PfaName, string Period, DeclarationType Type, Guid? DeclarationId, Guid? VersionId, string Reason);

/// <summary><c>GET /accounting/declarations/attention</c> — excepțiile din toate lunile, pentru Admin.</summary>
public sealed record GetDeclarationsAttentionQuery : IQuery<IReadOnlyList<DeclarationAttentionDto>>;

internal sealed class GetDeclarationsAttentionQueryHandler(IApplicationDbContext db) : IQueryHandler<GetDeclarationsAttentionQuery, IReadOnlyList<DeclarationAttentionDto>>
{
    public async Task<Result<IReadOnlyList<DeclarationAttentionDto>>> Handle(GetDeclarationsAttentionQuery query, CancellationToken cancellationToken)
    {
        var failed = await db.DeclarationVersions.AsNoTracking()
            .Where(v => v.VersionNo == v.Declaration.Versions.Max(other => other.VersionNo) &&
                        (v.Status == DeclarationStatus.Rejected || v.Status == DeclarationStatus.ValidationFailed))
            .Select(v => new { v.Declaration.PfaRegistrationId, v.Declaration.Period, v.Declaration.Type, v.DeclarationId, v.Id, v.Status })
            .ToListAsync(cancellationToken);
        var tasks = await db.DeclarationRectificationTasks.AsNoTracking()
            .Where(t => t.ResolvedAtUtc == null)
            .Join(db.Declarations, t => t.DeclarationId, d => d.Id, (t, d) => new { d.PfaRegistrationId, d.Period, d.Type, d.Id, t.DiffJson })
            .ToListAsync(cancellationToken);
        var waiting = await db.NonResidentPayments.AsNoTracking()
            .Join(db.NonResidentTaxDecisions.Where(d => d.Status == NonResidentDecisionStatus.NeedsLegalConfirmation), p => p.Id, d => d.PaymentId, (p, d) => new { p.PfaRegistrationId, p.PaymentDate })
            .ToListAsync(cancellationToken);

        List<Guid> pfaIds = [.. failed.Select(f => f.PfaRegistrationId).Concat(tasks.Select(t => t.PfaRegistrationId)).Concat(waiting.Select(w => w.PfaRegistrationId)).Distinct()];
        Dictionary<Guid, string> names = await db.PfaRegistrations.AsNoTracking()
            .Where(p => pfaIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.LegalName ?? p.FullName ?? string.Empty, cancellationToken);
        string Name(Guid id) => names.GetValueOrDefault(id, string.Empty);

        List<DeclarationAttentionDto> rows =
        [
            .. failed.Select(f => new DeclarationAttentionDto(
                f.PfaRegistrationId, Name(f.PfaRegistrationId), f.Period, f.Type, f.DeclarationId, f.Id,
                f.Status == DeclarationStatus.Rejected ? "Recipisă cu erori" : "Validare eșuată")),
            .. tasks.Select(t =>
            {
                RectificationDiff diff = AccountingJson.Deserialize(t.DiffJson, new RectificationDiff(0, 0, [], []));
                return new DeclarationAttentionDto(
                    t.PfaRegistrationId, Name(t.PfaRegistrationId), t.Period, t.Type, t.Id, null,
                    $"De rectificat: depus {AccountingJson.Amount(diff.SubmittedTotal)} lei, acum {AccountingJson.Amount(diff.CurrentTotal)} lei");
            }),
            .. waiting
                .GroupBy(w => (w.PfaRegistrationId, Period: LedgerPeriod(w.PaymentDate)))
                .Select(g => new DeclarationAttentionDto(g.Key.PfaRegistrationId, Name(g.Key.PfaRegistrationId), g.Key.Period, DeclarationType.D100, null, null,
                    g.Count() == 1 ? "Regula de nerezident de confirmat" : $"{g.Count()} reguli de nerezident de confirmat")),
        ];
        return rows.OrderBy(r => r.Period, StringComparer.Ordinal).ThenBy(r => r.PfaName, StringComparer.Ordinal).ToList();
    }

    private static string LedgerPeriod(DateOnly date) => Ledger.LedgerSupport.PeriodOf(date);
}

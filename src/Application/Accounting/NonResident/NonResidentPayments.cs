using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Ledger;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.NonResident;

/// <summary>
/// Plățile către nerezidenți și deciziile lor (spec declarații F20–F25). La Uber/Bolt, plata e
/// comisionul reținut la decontarea payout-ului (R21): data plății e data decontării, furnizorul
/// juridic vine din factura de comision a perioadei. O decizie confirmată de Admin nu se recalculează.
/// </summary>
internal static class NonResidentSync
{
    public static async Task SyncAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken)
    {
        await PaymentsAsync(db, pfaId, cancellationToken);
        await DecisionsAsync(db, pfaId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task PaymentsAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken)
    {
        List<Guid> recorded = await db.NonResidentPayments
            .Where(p => p.PfaRegistrationId == pfaId && p.LedgerEntryId != null)
            .Select(p => p.LedgerEntryId!.Value)
            .ToListAsync(cancellationToken);
        var commissions = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId &&
                        e.Category == LedgerSupport.PlatformCommissionCategory &&
                        e.SettlementGroupId != null &&
                        e.StornoOfEntryId == null &&
                        e.PlatformDocumentId != null &&
                        !recorded.Contains(e.Id))
            .Select(e => new { e.Id, e.Date, e.Amount, e.BankTransactionId, ReportId = e.PlatformDocumentId!.Value })
            .ToListAsync(cancellationToken);

        foreach (var commission in commissions)
        {
            var report = await db.PlatformDocuments.AsNoTracking()
                .Where(d => d.Id == commission.ReportId)
                .Select(d => new { d.Platform, d.Period })
                .SingleAsync(cancellationToken);

            // Factura de comision a aceleiași perioade identifică entitatea juridică (codul fiscal).
            var invoices = await db.DocumentExtractions.AsNoTracking()
                .Where(e => e.IsCurrent &&
                            e.PlatformDocument.PfaRegistrationId == pfaId &&
                            e.PlatformDocument.DocumentType == PlatformDocumentType.CommissionInvoice &&
                            e.PlatformDocument.Platform == report.Platform &&
                            e.PlatformDocument.Period == report.Period &&
                            e.PlatformDocument.DeletedAtUtc == null &&
                            (e.PlatformDocument.Status == PlatformDocumentStatus.Confirmed || e.PlatformDocument.Status == PlatformDocumentStatus.Locked) &&
                            e.SupplierVatId != null)
                .Select(e => new { e.PlatformDocumentId, e.SupplierVatId, e.SupplierName, e.SupplierCountry, e.TaxPointDate })
                .ToListAsync(cancellationToken);
            List<string> entities = [.. invoices.Select(i => i.SupplierVatId!.ToUpperInvariant()).Distinct()];
            if (entities.Count != 1)
            {
                // Fără entitate unică nu se ghicește furnizorul; plata apare după confirmarea facturii.
                continue;
            }

            var invoice = invoices.OrderBy(i => i.TaxPointDate).First();
            db.NonResidentPayments.Add(new NonResidentPayment
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = pfaId,
                BankTransactionId = commission.BankTransactionId,
                LedgerEntryId = commission.Id,
                CommissionInvoiceId = invoice.PlatformDocumentId,
                SupplierLegalName = invoice.SupplierName ?? entities[0],
                SupplierCountry = (invoice.SupplierCountry ?? entities[0][..2]).ToUpperInvariant(),
                SupplierTaxId = entities[0],
                PaymentDate = commission.Date,
                GrossIncomeRon = Math.Abs(commission.Amount),
                IncomeType = "COMMISSION",
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task DecisionsAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken)
    {
        List<NonResidentPayment> payments = await db.NonResidentPayments.Where(p => p.PfaRegistrationId == pfaId).ToListAsync(cancellationToken);
        if (payments.Count == 0)
        {
            return;
        }

        List<Guid> ids = [.. payments.Select(p => p.Id)];
        Dictionary<Guid, NonResidentTaxDecision> decisions = await db.NonResidentTaxDecisions
            .Where(d => ids.Contains(d.PaymentId))
            .ToDictionaryAsync(d => d.PaymentId, cancellationToken);
        List<SupplierTaxProfile> suppliers = await db.SupplierTaxProfiles.AsNoTracking().ToListAsync(cancellationToken);
        TaxRuleSet rules = await TaxRuleSet.LoadAsync(db, cancellationToken);

        foreach (NonResidentPayment payment in payments)
        {
            if (decisions.TryGetValue(payment.Id, out NonResidentTaxDecision? existing) && existing.Status == NonResidentDecisionStatus.Confirmed)
            {
                continue;
            }

            NonResidentDecisionResult result;
            try
            {
                result = NonResidentTaxEngine.Decide(payment, suppliers, rules);
            }
            catch (TaxRuleConfigurationException)
            {
                // Configurare incompletă: plata rămâne fără decizie, iar D100 așteaptă (nu se inventează).
                continue;
            }

            NonResidentTaxDecision decision = existing ?? new NonResidentTaxDecision { Id = Guid.NewGuid(), PaymentId = payment.Id };
            decision.ResidenceCertificateProfileId = result.CertificateProfileId;
            decision.TreatyRuleId = result.TreatyRuleId;
            decision.AppliedRuleId = result.AppliedRuleId;
            decision.TaxRate = result.Rate;
            decision.TaxDue = result.TaxDue;
            decision.ObligationCode = result.ObligationCode;
            decision.Status = result.Status;
            decision.Explanation = result.Explanation.Length > 1000 ? result.Explanation[..1000] : result.Explanation;
            decision.UpdatedAtUtc = DateTime.UtcNow;
            if (existing is null)
            {
                db.NonResidentTaxDecisions.Add(decision);
            }
        }
    }

    /// <summary>Plățile lunii cu decizia lor, intrarea D100 a motorului lunar (F20).</summary>
    public static async Task<ILookup<Guid, NonResidentLine>> LinesAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> pfaIds, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. pfaIds];
        var rows = await db.NonResidentPayments.AsNoTracking()
            .Where(p => ids.Contains(p.PfaRegistrationId) && p.PaymentDate >= start && p.PaymentDate <= end)
            .Join(db.NonResidentTaxDecisions, p => p.Id, d => d.PaymentId, (p, d) => new { Payment = p, Decision = d })
            .Select(row => new
            {
                row.Payment,
                row.Decision,
                Platform = db.PlatformDocuments.Where(doc => doc.Id == row.Payment.CommissionInvoiceId).Select(doc => doc.Platform).FirstOrDefault(),
                Certificate = db.SupplierTaxProfiles.Where(s => s.Id == row.Decision.ResidenceCertificateProfileId)
                    .Select(s => new { s.Treaty, s.ResidenceCertValidFrom, s.ResidenceCertValidTo })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows
            .OrderBy(row => row.Payment.PaymentDate)
            .ToLookup(row => row.Payment.PfaRegistrationId, row => new NonResidentLine(
                row.Payment.Id,
                row.Payment.CommissionInvoiceId,
                $"Comision {row.Payment.SupplierLegalName}, plata din {AccountingJson.Date(row.Payment.PaymentDate)}",
                row.Payment.PaymentDate,
                row.Payment.SupplierLegalName,
                row.Payment.SupplierCountry,
                row.Payment.SupplierTaxId,
                row.Payment.GrossIncomeRon,
                row.Decision.TaxRate,
                row.Decision.TaxDue,
                row.Decision.ObligationCode,
                row.Decision.Status,
                row.Decision.Explanation,
                row.Platform,
                row.Certificate?.Treaty,
                row.Certificate?.ResidenceCertValidFrom,
                row.Certificate?.ResidenceCertValidTo));
    }
}

/// <summary>O decizie de impozit nerezident, în coada Adminului.</summary>
public sealed record NonResidentDecisionDto(
    Guid Id,
    Guid PaymentId,
    Guid PfaId,
    string SupplierLegalName,
    string SupplierCountry,
    string SupplierTaxId,
    DateOnly PaymentDate,
    decimal GrossIncomeRon,
    decimal TaxRate,
    decimal TaxDue,
    string ObligationCode,
    NonResidentDecisionStatus Status,
    string Explanation,
    DateTime? ConfirmedAt,
    string? ConfirmationReason);

/// <summary><c>GET /accounting/pfas/{pfaId}/non-resident-decisions?status=</c> — coada <c>NeedsLegalConfirmation</c>.</summary>
public sealed record ListNonResidentDecisionsQuery(Guid PfaId, NonResidentDecisionStatus? Status) : IQuery<IReadOnlyList<NonResidentDecisionDto>>;

internal sealed class ListNonResidentDecisionsQueryHandler(IApplicationDbContext db)
    : IQueryHandler<ListNonResidentDecisionsQuery, IReadOnlyList<NonResidentDecisionDto>>
{
    public async Task<Result<IReadOnlyList<NonResidentDecisionDto>>> Handle(ListNonResidentDecisionsQuery query, CancellationToken cancellationToken)
    {
        await NonResidentSync.SyncAsync(db, query.PfaId, cancellationToken);
        return await db.NonResidentPayments.AsNoTracking()
            .Where(p => p.PfaRegistrationId == query.PfaId)
            .Join(db.NonResidentTaxDecisions, p => p.Id, d => d.PaymentId, (p, d) => new { p, d })
            .Where(row => query.Status == null || row.d.Status == query.Status)
            .OrderBy(row => row.p.PaymentDate)
            .Select(row => new NonResidentDecisionDto(
                row.d.Id, row.p.Id, row.p.PfaRegistrationId, row.p.SupplierLegalName, row.p.SupplierCountry, row.p.SupplierTaxId, row.p.PaymentDate,
                row.p.GrossIncomeRon, row.d.TaxRate, row.d.TaxDue, row.d.ObligationCode, row.d.Status, row.d.Explanation, row.d.ConfirmedAtUtc, row.d.ConfirmationReason))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// <c>POST /accounting/non-resident-decisions/{id}/confirm</c> — <c>{ reason }</c> (F23): Adminul
/// confirmă regula aplicată; D100 lunii poate fi generată.
/// </summary>
public sealed record ConfirmNonResidentDecisionCommand(Guid DecisionId, string? Reason) : ICommand<NonResidentDecisionDto>;

internal sealed class ConfirmNonResidentDecisionCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<ConfirmNonResidentDecisionCommand, NonResidentDecisionDto>
{
    public static readonly Error NotFound = Error.NotFound("Accounting.NonResidentDecisionNotFound", "Decizia nu există.");

    public async Task<Result<NonResidentDecisionDto>> Handle(ConfirmNonResidentDecisionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<NonResidentDecisionDto>(AccountingErrors.ReasonRequired);
        }

        NonResidentTaxDecision? decision = await db.NonResidentTaxDecisions.SingleOrDefaultAsync(d => d.Id == command.DecisionId, cancellationToken);
        if (decision is null)
        {
            return Result.Failure<NonResidentDecisionDto>(NotFound);
        }

        NonResidentPayment payment = await db.NonResidentPayments.SingleAsync(p => p.Id == decision.PaymentId, cancellationToken);
        NonResidentDecisionStatus before = decision.Status;
        decision.Status = NonResidentDecisionStatus.Confirmed;
        decision.ConfirmedByUserId = userContext.UserId;
        decision.ConfirmedAtUtc = DateTime.UtcNow;
        decision.ConfirmationReason = command.Reason.Trim();
        decision.UpdatedAtUtc = DateTime.UtcNow;
        AccountingAudit.Record(db, payment.PfaRegistrationId, nameof(NonResidentTaxDecision), decision.Id, "CONFIRM",
            new { status = before }, new { status = decision.Status, decision.TaxRate, decision.TaxDue }, decision.ConfirmationReason, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return new NonResidentDecisionDto(
            decision.Id, payment.Id, payment.PfaRegistrationId, payment.SupplierLegalName, payment.SupplierCountry, payment.SupplierTaxId, payment.PaymentDate,
            payment.GrossIncomeRon, decision.TaxRate, decision.TaxDue, decision.ObligationCode, decision.Status, decision.Explanation, decision.ConfirmedAtUtc,
            decision.ConfirmationReason);
    }
}

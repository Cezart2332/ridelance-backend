using System.Globalization;
using System.Text.RegularExpressions;
using Application.Abstractions.Data;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Ledger;

/// <summary>
/// Facturile primite prin RO e-Factura și plățile lor din bancă (spec flux contabil R03–R04b).
/// </summary>
/// <remarks>
/// <para>
/// Factura singură nu creează nimic în ledger (R03): apare în RJIP doar plata ei. Plata bancară se leagă
/// de factură când e sigur: aceeași sumă ca restul de plată, furnizorul potrivit (nume, CUI sau
/// numărul facturii în detaliile plății), plătită în ziua facturii sau după (R04). O plată mai mică,
/// cu numărul facturii în detalii, e o plată parțială (R04b).
/// </para>
/// <para>
/// Un match ambiguu nu se aplică: mai multe facturi pentru aceeași plată sau mai multe plăți pentru
/// aceeași factură rămân nelegate, cu o notă.
/// </para>
/// </remarks>
internal sealed class EFacturaLedgerSource(IApplicationDbContext db) : ILedgerSource
{
    public int Order => 3;

    public async Task<LedgerImportResult> ImportAsync(LedgerImportContext context, CancellationToken cancellationToken)
    {
        List<EFacturaMessage> invoices = await db.EFacturaMessages
            .Where(m => m.PfaRegistrationId == context.PfaId &&
                        m.Kind == EFacturaMessageKind.Received &&
                        !m.IsCreditNote &&
                        m.TotalAmount > 0 &&
                        m.IssueDate != null &&
                        (m.Currency == null || m.Currency == "RON") &&
                        m.PaymentStatus != InvoicePaymentStatus.Paid)
            .ToListAsync(cancellationToken);
        if (invoices.Count == 0)
        {
            return new LedgerImportResult(LedgerSource.EFactura, 0, 0, []);
        }

        DateOnly earliest = invoices.Min(i => i.IssueDate!.Value);
        List<LedgerEntry> payments = await db.LedgerEntries
            .Where(e => e.PfaRegistrationId == context.PfaId &&
                        e.Source == LedgerSource.Bank &&
                        e.TransactionType == LedgerTransactionType.Expense &&
                        e.BankTransactionId != null &&
                        e.EFacturaMessageId == null &&
                        e.SourceDocumentId == null &&
                        e.ReconciliationStatus == ReconciliationStatus.Unmatched &&
                        e.Status != LedgerEntryStatus.Locked &&
                        !e.ClosedPeriodFlag &&
                        e.Date >= earliest)
            .ToListAsync(cancellationToken);

        // Candidații în ambele sensuri: o legătură se aplică doar când e singura posibilă.
        var candidates = new List<(EFacturaMessage Invoice, LedgerEntry Payment, bool Partial)>();
        foreach (EFacturaMessage invoice in invoices)
        {
            decimal due = invoice.TotalAmount!.Value - invoice.PaidAmount;
            foreach (LedgerEntry payment in payments.Where(p => p.Date >= invoice.IssueDate!.Value))
            {
                decimal paid = Math.Abs(payment.Amount);
                bool numberInDetails = MentionsNumber(payment, invoice.InvoiceNumber);
                if (paid == due && (SameSupplier(payment, invoice) || numberInDetails))
                {
                    candidates.Add((invoice, payment, false));
                }
                else if (paid < due && numberInDetails)
                {
                    candidates.Add((invoice, payment, true));
                }
            }
        }

        int updated = 0;
        var notes = new List<string>();
        foreach ((EFacturaMessage Invoice, LedgerEntry Payment, bool Partial) candidate in candidates)
        {
            bool unique = candidates.Count(c => c.Payment == candidate.Payment) == 1 &&
                          candidates.Count(c => c.Invoice == candidate.Invoice) == 1;
            if (!unique)
            {
                notes.Add($"Plata de {AccountingJson.Amount(Math.Abs(candidate.Payment.Amount))} lei din {Format(candidate.Payment.Date)} se potrivește cu mai multe facturi sau plăți; se asociază manual.");
                continue;
            }

            Link(candidate.Invoice, candidate.Payment, candidate.Partial);
            DeductibilityService.Resolve(candidate.Payment, context.Rules);
            updated++;
        }

        return new LedgerImportResult(LedgerSource.EFactura, 0, updated, [.. notes.Distinct()]);
    }

    /// <summary>R04 / R04b: plata devine „plata facturii”, factura își actualizează suma plătită.</summary>
    private static void Link(EFacturaMessage invoice, LedgerEntry payment, bool partial)
    {
        string supplier = invoice.SupplierName ?? "furnizor";
        payment.EFacturaMessageId = invoice.Id;
        payment.DocumentDate = invoice.IssueDate;
        payment.Counterparty ??= LedgerSupport.Cut(supplier, LedgerSupport.CounterpartyLength);
        payment.Description = LedgerSupport.Cut(
            $"{(partial ? "Plata parțială a facturii" : "Plata facturii")} {invoice.InvoiceNumber} {supplier}".Trim(),
            LedgerSupport.DescriptionLength);
        payment.ReconciliationStatus = partial ? ReconciliationStatus.Partial : ReconciliationStatus.Matched;

        invoice.PaidAmount = LedgerInvariants.Round(invoice.PaidAmount + Math.Abs(payment.Amount));
        invoice.PaymentStatus = invoice.PaidAmount >= invoice.TotalAmount ? InvoicePaymentStatus.Paid : InvoicePaymentStatus.PartiallyPaid;
    }

    /// <summary>Furnizorul facturii e contrapartida plății: CUI-ul în detalii sau numele normalizat.</summary>
    private static bool SameSupplier(LedgerEntry payment, EFacturaMessage invoice)
    {
        string cui = new([.. (invoice.SupplierCif ?? string.Empty).Where(char.IsDigit)]);
        if (cui.Length >= 2 && (payment.Description + " " + payment.Counterparty).Contains(cui, StringComparison.Ordinal))
        {
            return true;
        }

        string supplier = CounterpartyRules.NormalizeMerchant(invoice.SupplierName);
        string counterparty = CounterpartyRules.NormalizeMerchant(payment.Counterparty);
        return supplier.Length >= 3 && counterparty.Length >= 3 &&
               (supplier.Contains(counterparty, StringComparison.Ordinal) || counterparty.Contains(supplier, StringComparison.Ordinal));
    }

    /// <summary>
    /// Numărul facturii apare în detaliile plății („OP fact.1234”) ca număr întreg: „123” nu se
    /// găsește în „4123”. Un număr cu serie și spațiu („ABC 1234”) se caută și ca atare.
    /// </summary>
    private static bool MentionsNumber(LedgerEntry payment, string? number)
    {
        string needle = number?.Trim() ?? string.Empty;
        string compact = Regex.Replace(needle.ToUpperInvariant(), "[^A-Z0-9]", string.Empty, RegexOptions.None, PatternTimeout);
        if (compact.Length < 3)
        {
            return false;
        }

        string details = payment.Description.ToUpperInvariant();
        string[] words = Regex.Split(details, "[^A-Z0-9]+", RegexOptions.None, PatternTimeout);
        return words.Contains(compact, StringComparer.Ordinal) ||
               needle.Any(ch => !char.IsLetterOrDigit(ch)) && details.Contains(needle.ToUpperInvariant(), StringComparison.Ordinal);
    }

    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);

    private static string Format(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}

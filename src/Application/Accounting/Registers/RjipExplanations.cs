using Application.Abstractions.Data;
using Application.Accounting.Contracts;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Registers;

/// <summary>De ce o înregistrare e o excepție a RJIP (de rezolvat înainte de închiderea lunii).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(UpperSnakeCaseEnumConverter<RegisterExceptionKind>))]
public enum RegisterExceptionKind
{
    UnidentifiedIncome = 0,
    UnclassifiedPayment = 1,
    MissingDocument = 2,
    TransferToConfirm = 3,
    UnreconciledPayout = 4,
}

/// <summary>Tranzacția bancară a unei înregistrări: referința de la provider și textul brut al băncii.</summary>
internal sealed record BankReference(string ProviderTransactionId, string? Details);

/// <summary>Ce mai trebuie RJIP-ului în afară de ledger: etichetele categoriilor și tranzacțiile bancare.</summary>
internal sealed record RjipSources(IReadOnlyDictionary<string, string> CategoryLabels, IReadOnlyDictionary<Guid, BankReference> Bank)
{
    public static readonly RjipSources Empty = new(new Dictionary<string, string>(), new Dictionary<Guid, BankReference>());

    public static async Task<RjipSources> LoadAsync(IApplicationDbContext db, IEnumerable<RegisterEntry> entries, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. entries.Select(e => e.Entry.BankTransactionId).OfType<Guid>().Distinct()];
        var transactions = ids.Count == 0
            ? []
            : await db.BankTransactions.AsNoTracking()
                .Where(t => ids.Contains(t.Id))
                .Select(t => new { t.Id, t.ProviderTransactionId, t.CounterpartyName, t.RemittanceInfo })
                .ToListAsync(cancellationToken);
        Dictionary<string, string> labels = await db.ExpenseCategoryRules.AsNoTracking()
            .GroupBy(r => r.Category)
            .Select(g => new { Category = g.Key, Label = g.OrderByDescending(r => r.ValidFrom).Select(r => r.Label).First() })
            .ToDictionaryAsync(r => r.Category, r => r.Label, cancellationToken);
        return new RjipSources(
            labels,
            transactions.ToDictionary(
                t => t.Id,
                t => new BankReference(
                    t.ProviderTransactionId,
                    string.Join(" · ", new[] { t.CounterpartyName, t.RemittanceInfo }.Where(text => !string.IsNullOrWhiteSpace(text))))));
    }
}

/// <summary>
/// Explicațiile și documentele RJIP (model 14-1-1/b): explicația vine din clasificarea înregistrării,
/// niciodată din textul băncii; documentul are felul și numărul (extrasul cu referința tranzacției).
/// </summary>
internal static class RjipExplanations
{
    /// <summary>Excepția înregistrării, aceeași regulă ca numărătoarea din panoul registrelor.</summary>
    public static RegisterExceptionKind? ExceptionOf(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.StornoOfEntryId is not null || entry.ClosedPeriodFlag)
        {
            return null;
        }

        if (entry.TransactionType == LedgerTransactionType.PlatformSettlement)
        {
            return RegisterExceptionKind.UnreconciledPayout;
        }

        return entry.ReconciliationStatus switch
        {
            ReconciliationStatus.NeedsReview when entry.ProposedClassification is { } proposal && BankClassifications.IsTransfer(proposal) => RegisterExceptionKind.TransferToConfirm,
            ReconciliationStatus.NeedsReview => entry.Amount > 0 ? RegisterExceptionKind.UnidentifiedIncome : RegisterExceptionKind.UnclassifiedPayment,
            ReconciliationStatus.Unmatched => RegisterExceptionKind.MissingDocument,
            _ => null,
        };
    }

    /// <summary>
    /// Explicația contabilă. Înregistrările care nu vin din bancă (bon, raport Z, payout reconciliat,
    /// factură e-Factura sau Oblio) au deja descrierea scrisă de sistem.
    /// </summary>
    public static string Explain(LedgerEntry entry, RjipSources sources)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(sources);
        bool fromBankText = entry.Source == LedgerSource.Bank &&
                            entry.EFacturaMessageId is null && entry.SettlementGroupId is null &&
                            !(entry.TransactionType == LedgerTransactionType.Income && entry.DocumentLabel.StartsWith("Factura", StringComparison.OrdinalIgnoreCase));
        if (!fromBankText)
        {
            return entry.Counterparty is { Length: > 0 } counterparty && !entry.Description.Contains(counterparty, StringComparison.OrdinalIgnoreCase)
                ? $"{entry.Description} – {counterparty}"
                : entry.Description;
        }

        if (entry.ReconciliationStatus == ReconciliationStatus.NeedsReview)
        {
            string state = entry.ProposedClassification is null ? "neidentificată" : "neclasificată";
            return entry.Amount > 0 ? $"Încasare {state}" : $"Plată {state}";
        }

        bool documented = entry.DocumentLabel is { Length: > 0 } document && !document.StartsWith("Extras", StringComparison.OrdinalIgnoreCase);
        string text = BankClassifications.Of(entry) switch
        {
            BankClassification.Expense when entry.Category is { } category => sources.CategoryLabels.GetValueOrDefault(category, "Cheltuială din activitate"),
            BankClassification.Expense => documented ? "Cheltuială" : "Cheltuială neclasificată",
            { } classification => BankClassifications.Label(classification),
            null => entry.TransactionType switch
            {
                LedgerTransactionType.Loan => "Împrumut",
                LedgerTransactionType.Transfer => "Transfer",
                _ => entry.Amount > 0 ? "Încasare neidentificată" : "Plată neidentificată",
            },
        };

        // Justificativul plății (bonul, factura) stă în explicație; documentul RJIP e extrasul.
        if (documented)
        {
            string label = entry.DocumentLabel;
            text += $", {char.ToLowerInvariant(label[0])}{label[1..]}";
        }

        return text;
    }

    /// <summary>Documentul (fel, număr): la bancă extrasul cu referința tranzacției, în numerar justificativul.</summary>
    public static string Document(LedgerEntry entry, RjipSources sources)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(sources);
        if (entry.PaymentMethod != PaymentMethod.Bank)
        {
            return entry.DocumentLabel;
        }

        if (entry.BankTransactionId is not { } id)
        {
            return GetRjipQueryHandler.BankStatement;
        }

        string provider = sources.Bank.TryGetValue(id, out BankReference? bank) ? bank.ProviderTransactionId : string.Empty;
        return $"{GetRjipQueryHandler.BankStatement}, ref. {Short(provider.Length > 0 ? provider : id.ToString("N"))}";
    }

    /// <summary>Textul băncii, doar pentru detaliul din ecran (nu intră în registru sau în export).</summary>
    public static string? BankDetails(LedgerEntry entry, RjipSources sources)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(sources);
        if (entry.BankTransactionId is not { } id)
        {
            return null;
        }

        return sources.Bank.TryGetValue(id, out BankReference? bank) && !string.IsNullOrWhiteSpace(bank.Details) ? bank.Details : entry.Description;
    }

    /// <summary>Ultimele 8 caractere alfanumerice ale referinței, cu majuscule.</summary>
    private static string Short(string reference)
    {
        string compact = new([.. reference.Where(char.IsLetterOrDigit)]);
        return (compact.Length <= 8 ? compact : compact[^8..]).ToUpperInvariant();
    }
}

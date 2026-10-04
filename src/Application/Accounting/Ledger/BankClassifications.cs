using Domain.Accounting;

namespace Application.Accounting.Ledger;

/// <summary>
/// Clasificarea unei tranzacții bancare (spec flux contabil §6): ce tip de înregistrare devine, ce
/// explicație are în RJIP și dacă mai cere document. Propunerea din contrapartidă nu se aplică până la
/// confirmare; regula învățată se aplică direct.
/// </summary>
public static class BankClassifications
{
    /// <summary>Categoria de cheltuială a comisionului bancar (<see cref="ExpenseCategoryRule"/>).</summary>
    public const string BankFeeCategory = "BANK_FEES";

    /// <summary>Încasare sau plată: fiecare clasificare are un sens.</summary>
    public static bool Fits(BankClassification classification, decimal amount) => classification switch
    {
        BankClassification.OwnerContribution or BankClassification.ActivityIncome or BankClassification.NonTaxable => amount > 0,
        BankClassification.OwnerWithdrawal or BankClassification.TaxPayment or BankClassification.BankFee or BankClassification.Expense => amount < 0,
        _ => amount != 0,
    };

    /// <summary>Clasificările posibile pentru sensul tranzacției, în ordinea din meniul de clasificare.</summary>
    public static IReadOnlyList<BankClassification> OptionsFor(decimal amount) =>
        [.. Enum.GetValues<BankClassification>().Where(classification => Fits(classification, amount))];

    /// <summary>Explicația contabilă, cum apare în RJIP.</summary>
    public static string Label(BankClassification classification) => classification switch
    {
        BankClassification.OwnerContribution => "Aport titular",
        BankClassification.OwnerWithdrawal => "Transfer către titular",
        BankClassification.InternalTransfer => "Transfer între conturile PFA",
        BankClassification.TaxPayment => "Plată impozite/contribuții ANAF",
        BankClassification.BankFee => "Comision administrare cont bancar",
        BankClassification.ActivityIncome => "Încasare venit din activitate",
        BankClassification.NonTaxable => "Încasare neimpozabilă",
        _ => "Cheltuială din activitate",
    };

    /// <summary>Propunerea din contrapartidă, dacă există una.</summary>
    public static BankClassification? FromKind(CounterpartyKind kind) => kind switch
    {
        CounterpartyKind.OwnerContribution => BankClassification.OwnerContribution,
        CounterpartyKind.OwnerWithdrawal => BankClassification.OwnerWithdrawal,
        CounterpartyKind.InternalTransfer => BankClassification.InternalTransfer,
        CounterpartyKind.Tax => BankClassification.TaxPayment,
        CounterpartyKind.BankFee => BankClassification.BankFee,
        _ => null,
    };

    /// <summary>Transferurile cu titularul sau între conturi: se confirmă separat de restul.</summary>
    public static bool IsTransfer(BankClassification classification) =>
        classification is BankClassification.OwnerContribution or BankClassification.OwnerWithdrawal or BankClassification.InternalTransfer;

    /// <summary>
    /// Propunerea: înregistrarea rămâne neutră fiscal (fără venit, fără deductibil) și de verificat
    /// până la confirmare. Excepție în RJIP, blochează închiderea lunii.
    /// </summary>
    public static void Propose(LedgerEntry entry, BankClassification classification)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.TransactionType = LedgerTransactionType.Other;
        entry.Category = null;
        entry.ProposedClassification = classification;
        entry.ReconciliationStatus = ReconciliationStatus.NeedsReview;
        if (!entry.ClosedPeriodFlag)
        {
            entry.Status = LedgerEntryStatus.NeedsReview;
        }
    }

    /// <summary>Aplică clasificarea: tipul, categoria și starea; deductibilul se recalculează.</summary>
    public static void Apply(LedgerEntry entry, BankClassification classification, LedgerRules rules, bool verified)
    {
        ArgumentNullException.ThrowIfNull(entry);
        (LedgerTransactionType type, string? category) = classification switch
        {
            BankClassification.OwnerContribution => (LedgerTransactionType.OwnerContribution, null),
            BankClassification.OwnerWithdrawal => (LedgerTransactionType.OwnerWithdrawal, null),
            BankClassification.InternalTransfer => (LedgerTransactionType.InternalTransfer, null),
            BankClassification.TaxPayment => (LedgerTransactionType.Tax, null),
            BankClassification.BankFee => (LedgerTransactionType.Expense, BankFeeCategory),
            BankClassification.ActivityIncome => (LedgerTransactionType.Income, null),
            BankClassification.NonTaxable => (LedgerTransactionType.Other, null),
            _ => (LedgerTransactionType.Expense, entry.Category),
        };

        entry.TransactionType = type;
        entry.Category = category;
        entry.ProposedClassification = null;

        // O cheltuială obișnuită cere documentul (R01); restul au extrasul drept justificativ.
        entry.ReconciliationStatus = classification == BankClassification.Expense ? ReconciliationStatus.Unmatched : ReconciliationStatus.Matched;
        if (!entry.ClosedPeriodFlag)
        {
            entry.Status = verified ? LedgerEntryStatus.Verified : LedgerEntryStatus.AutoImported;
        }

        DeductibilityService.Resolve(entry, rules);
    }

    /// <summary>Clasificarea deja aplicată pe o înregistrare bancară, dacă tipul o spune.</summary>
    public static BankClassification? Of(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.TransactionType switch
        {
            LedgerTransactionType.OwnerContribution => BankClassification.OwnerContribution,
            LedgerTransactionType.OwnerWithdrawal => BankClassification.OwnerWithdrawal,
            LedgerTransactionType.InternalTransfer => BankClassification.InternalTransfer,
            LedgerTransactionType.Tax => BankClassification.TaxPayment,
            LedgerTransactionType.Expense when entry.Category == BankFeeCategory => BankClassification.BankFee,
            LedgerTransactionType.Expense => BankClassification.Expense,
            LedgerTransactionType.Income => BankClassification.ActivityIncome,
            LedgerTransactionType.Other when entry.ReconciliationStatus == ReconciliationStatus.Matched => BankClassification.NonTaxable,
            _ => null,
        };
    }

    /// <summary>Regula învățată care se potrivește tranzacției: același sens și același IBAN sau aceeași cheie de nume.</summary>
    public static CounterpartyClassificationRule? Match(
        IEnumerable<CounterpartyClassificationRule> rules, decimal amount, string? counterpartyName, string? counterpartyIban, string? remittance)
    {
        ArgumentNullException.ThrowIfNull(rules);
        string key = CounterpartyRules.NameKey(counterpartyName, remittance);
        string? iban = CounterpartyRules.NormalizeIban(counterpartyIban);
        return rules
            .Where(rule => rule.Incoming == amount > 0 && Fits(rule.Classification, amount))
            .OrderByDescending(rule => rule.CreatedAtUtc)
            .FirstOrDefault(rule => iban is not null && rule.Iban == iban || key.Length >= 3 && rule.NameKey == key);
    }
}

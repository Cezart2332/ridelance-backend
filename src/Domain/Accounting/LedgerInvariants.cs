namespace Domain.Accounting;

/// <summary>
/// Invarianții ledger-ului (spec flux contabil §4). Se verifică în domeniu, la fiecare salvare, nu
/// doar în interfață: o înregistrare care îi încalcă nu ajunge în bază.
/// </summary>
public static class LedgerInvariants
{
    /// <summary>Rotunjirea contabilă: 2 zecimale, jumătatea se rotunjește în sus (în valoare absolută).</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Încălcările unei singure înregistrări; lista goală înseamnă validă.</summary>
    public static IReadOnlyList<string> Check(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        List<string> violations = [];
        decimal amount = Math.Abs(entry.Amount);

        if (Round(entry.Amount) != entry.Amount)
        {
            violations.Add("Suma are mai mult de două zecimale.");
        }

        if (entry.PersonalAmount < 0 || entry.PersonalAmount > amount)
        {
            violations.Add("Partea personală trebuie să fie între 0 și suma înregistrării.");
        }
        else if (Round(entry.PersonalAmount) != entry.PersonalAmount)
        {
            violations.Add("Partea personală are mai mult de două zecimale.");
        }

        // Partea personală intră integral la nedeductibil, deci deductibilul încape în partea business.
        // Stornarea are deductibilul cu semn opus, ca suma.
        if (entry.DeductibleAmount is { } value &&
            (entry.StornoOfEntryId is null ? value : -value) is var deductible &&
            (deductible < 0 || deductible > entry.BusinessAmount))
        {
            violations.Add("Suma deductibilă trebuie să fie între 0 și partea din activitate (suma fără partea personală).");
        }

        return violations;
    }

    /// <summary>
    /// Înregistrările legate de aceeași tranzacție bancară: una singură, sau mai multe din același
    /// grup de decontare (venit brut + comision, R21). Suma lor netă e suma tranzacției.
    /// </summary>
    public static IReadOnlyList<string> CheckBankLinks(decimal transactionAmount, IReadOnlyCollection<LedgerEntry> linked)
    {
        ArgumentNullException.ThrowIfNull(linked);
        List<string> violations = [];
        if (linked.Count == 0)
        {
            return violations;
        }

        if (linked.Count > 1 && (linked.Any(e => e.SettlementGroupId is null) || linked.Select(e => e.SettlementGroupId).Distinct().Count() > 1))
        {
            violations.Add("O tranzacție bancară se poate împărți între mai multe înregistrări doar în același grup de decontare.");
        }

        decimal net = linked.Sum(e => e.Amount);
        if (net != transactionAmount)
        {
            violations.Add($"Înregistrările legate de tranzacție însumează {net:0.00}, nu {transactionAmount:0.00}.");
        }

        return violations;
    }

    /// <summary>
    /// Plățile unui document: totalul lor nu depășește documentul. Plățile parțiale (R04b) sunt
    /// permise doar la facturi; bonul și documentul manual au cel mult o plată, prin schemă.
    /// </summary>
    public static IReadOnlyList<string> CheckPayments(decimal documentTotal, IEnumerable<decimal> payments)
    {
        decimal paid = payments.Sum(Math.Abs);
        return paid > Math.Abs(documentTotal)
            ? [$"Plățile documentului ({paid:0.00}) depășesc totalul lui ({Math.Abs(documentTotal):0.00})."]
            : [];
    }

    /// <summary>Aruncă dacă înregistrarea încalcă un invariant: o greșeală de program, nu o operațiune permisă.</summary>
    public static void EnsureValid(LedgerEntry entry)
    {
        IReadOnlyList<string> violations = Check(entry);
        if (violations.Count > 0)
        {
            throw new LedgerInvariantException(entry.Id, violations);
        }
    }
}

/// <summary>O înregistrare din ledger care încalcă un invariant (spec flux contabil §4).</summary>
public sealed class LedgerInvariantException(Guid entryId, IReadOnlyList<string> violations)
    : InvalidOperationException($"Înregistrarea {entryId} încalcă invarianții ledger-ului: {string.Join(" ", violations)}")
{
    public Guid EntryId { get; } = entryId;

    public IReadOnlyList<string> Violations { get; } = violations;
}

using Application.FiscalEstimates;
using Domain.Accounting;

namespace Application.Accounting.Annual;

/// <summary>O plată către nerezident din an, cu decizia ei (sursa D207, spec declarații F30).</summary>
public sealed record D207Payment(
    Guid PaymentId,
    DateOnly PaymentDate,
    string SupplierName,
    string SupplierCountry,
    string SupplierTaxId,
    string IncomeType,
    decimal GrossIncomeRon,
    decimal TaxRate,
    decimal TaxDue,
    NonResidentDecisionStatus Status,
    string? Treaty);

/// <summary>Impozitul unui beneficiar dintr-o D100 lunară generată (versiunea curentă).</summary>
public sealed record D100Declared(string Period, string SupplierTaxId, decimal Tax);

/// <summary>Un beneficiar nerezident în D207: totalurile anului (F30), inclusiv venitul scutit (F31).</summary>
public sealed record D207Beneficiary(
    string SupplierName,
    string Country,
    string TaxId,
    string IncomeType,
    decimal GrossIncome,
    decimal TaxWithheld,
    decimal ExemptIncome,
    string? Treaty,
    int Payments,
    decimal DeclaredInD100);

public sealed record D207DataModel(int Year, IReadOnlyList<D207Beneficiary> Beneficiaries, decimal TotalGross, decimal TotalTax, decimal? PaidTotal);

/// <summary>Rezultatul unei declarații anuale: modelul, ce o oprește și ce trebuie revăzut.</summary>
public sealed record AnnualCalculation<T>(T Model, IReadOnlyList<string> Blockers, IReadOnlyList<string> Review)
{
    public bool IsBlocked => Blockers.Count > 0;
}

/// <summary>
/// D207 (spec declarații F30–F32): agregatul anual al deciziilor de impozit nerezident, pe
/// beneficiar. Controlul: impozitul fiecărui beneficiar = Σ D100 lunare, iar brutul plăților =
/// Σ plăților efective din ledger. O decizie încă de confirmat sau o diferență oprește D207.
/// </summary>
public static class D207Engine
{
    /// <param name="paidTotal">Σ comisioanelor reținute la decontare în an (ledger); <c>null</c> = fără control.</param>
    public static AnnualCalculation<D207DataModel> Build(int year, IReadOnlyList<D207Payment> payments, IReadOnlyList<D100Declared> declared, decimal? paidTotal)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(declared);
        var blockers = new List<string>();

        int waiting = payments.Count(p => p.Status == NonResidentDecisionStatus.NeedsLegalConfirmation);
        if (waiting > 0)
        {
            blockers.Add(waiting == 1 ? "O regulă de nerezident e de confirmat." : $"{waiting} reguli de nerezident sunt de confirmat.");
        }

        var declaredByBeneficiary = declared
            .GroupBy(d => d.SupplierTaxId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Tax), StringComparer.OrdinalIgnoreCase);

        List<D207Beneficiary> beneficiaries = [.. payments
            .GroupBy(p => (TaxId: p.SupplierTaxId.ToUpperInvariant(), p.IncomeType))
            .OrderBy(g => g.Key.TaxId, StringComparer.Ordinal)
            .Select(g =>
            {
                D207Payment first = g.OrderByDescending(p => p.PaymentDate).First();
                return new D207Beneficiary(
                    first.SupplierName,
                    first.SupplierCountry,
                    g.Key.TaxId,
                    g.Key.IncomeType,
                    g.Sum(p => p.GrossIncomeRon),
                    g.Sum(p => p.TaxDue),
                    // F31: venitul scutit (impozit 0, de regulă prin tratat) intră în D207.
                    g.Where(p => p.TaxDue == 0).Sum(p => p.GrossIncomeRon),
                    first.Treaty,
                    g.Count(),
                    declaredByBeneficiary.GetValueOrDefault(g.Key.TaxId));
            })];

        // F32: totalul pe beneficiar = Σ D100 lunare.
        foreach (D207Beneficiary beneficiary in beneficiaries.Where(b => b.TaxWithheld != b.DeclaredInD100))
        {
            blockers.Add(
                $"{beneficiary.SupplierName}: impozitul anului e {AccountingJson.Amount(beneficiary.TaxWithheld)} lei, " +
                $"în D100 lunare {AccountingJson.Amount(beneficiary.DeclaredInD100)} lei.");
        }

        foreach (string orphan in declaredByBeneficiary.Keys.Where(id => beneficiaries.All(b => !string.Equals(b.TaxId, id, StringComparison.OrdinalIgnoreCase))))
        {
            blockers.Add($"D100 are impozit pentru {orphan}, fără plăți în registrul anual de nerezidenți.");
        }

        decimal gross = beneficiaries.Sum(b => b.GrossIncome);
        if (paidTotal is { } paid && paid != gross)
        {
            blockers.Add($"Plățile către nerezidenți din registru însumează {AccountingJson.Amount(gross)} lei, comisioanele decontate {AccountingJson.Amount(paid)} lei.");
        }

        return new(new D207DataModel(year, beneficiaries, gross, beneficiaries.Sum(b => b.TaxWithheld), paidTotal), blockers, []);
    }
}

/// <summary>O plată de chirie cu reținerea calculată (F41), sursa D205 (F42).</summary>
public sealed record RentWithholding(
    Guid PaymentId,
    Guid ContractId,
    string ContractNumber,
    string OwnerName,
    string OwnerCnpMasked,
    DateOnly PaymentDate,
    decimal Gross,
    decimal Rate,
    decimal Tax,
    bool WithholdOnPayment,
    bool RuleConfirmed);

public sealed record D205Beneficiary(string OwnerName, string OwnerCnpMasked, string ContractNumber, decimal GrossIncome, decimal TaxWithheld, int Payments);

public sealed record D205DataModel(int Year, IReadOnlyList<D205Beneficiary> Beneficiaries, decimal TotalGross, decimal TotalTax);

/// <summary>
/// Ramura de chirie de la persoane fizice (spec declarații F40–F43). Regula de reținere e o
/// <see cref="TaxRuleTypes.RentWithholding"/>, niciodată una de nerezident (F43).
/// </summary>
public static class RentEngine
{
    /// <summary>Formula regulii care cere reținerea la plată (rând D100 în luna plății, F41).</summary>
    public const string WithholdOnPayment = "WITHHOLD_ON_PAYMENT";

    /// <summary>F43: regula unui contract trebuie să fie de chirie.</summary>
    /// <exception cref="Tax.TaxRuleConfigurationException">Regula e de alt tip (de ex. de nerezident).</exception>
    public static void EnsureRentRule(TaxRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.RuleType != TaxRuleTypes.RentWithholding)
        {
            throw new Tax.TaxRuleConfigurationException(
                $"Contractul de chirie folosește regula {rule.Id} de tip {rule.RuleType}; chiria cere o regulă {TaxRuleTypes.RentWithholding}, separată de cea de nerezident.");
        }
    }

    /// <summary>F41: reținerea unei plăți, cu regula contractului.</summary>
    public static RentWithholding Withhold(RentalContract contract, RentPayment payment, TaxRule rule, string ownerCnpMasked)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(payment);
        EnsureRentRule(rule);
        decimal rate = rule.Rate ?? 0;
        return new RentWithholding(
            payment.Id,
            contract.Id,
            contract.ContractNumber,
            contract.OwnerName,
            ownerCnpMasked,
            payment.PaymentDate,
            payment.GrossAmount,
            rate,
            Math.Round(payment.GrossAmount * rate / 100m, 2, MidpointRounding.AwayFromZero),
            string.Equals(rule.Formula, WithholdOnPayment, StringComparison.OrdinalIgnoreCase),
            rule.Confirmed);
    }

    /// <summary>F42: D205 pe beneficiar din plățile de chirie ale anului.</summary>
    public static AnnualCalculation<D205DataModel> D205(int year, IReadOnlyList<RentWithholding> payments, bool deadlineConfirmed)
    {
        ArgumentNullException.ThrowIfNull(payments);
        var blockers = new List<string>();
        if (payments.Any(p => !p.RuleConfirmed))
        {
            blockers.Add("Regula de reținere pentru chirie e de confirmat juridic.");
        }

        if (!deadlineConfirmed)
        {
            blockers.Add("Termenul D205 e de confirmat juridic.");
        }

        List<D205Beneficiary> beneficiaries = [.. payments
            .GroupBy(p => p.ContractId)
            .Select(g => new D205Beneficiary(g.First().OwnerName, g.First().OwnerCnpMasked, g.First().ContractNumber, g.Sum(p => p.Gross), g.Sum(p => p.Tax), g.Count()))
            .OrderBy(b => b.OwnerName, StringComparer.Ordinal)];
        return new(new D205DataModel(year, beneficiaries, beneficiaries.Sum(b => b.GrossIncome), beneficiaries.Sum(b => b.TaxWithheld)), blockers, []);
    }
}

/// <summary>Venitul brut și cheltuielile deductibile ale anului.</summary>
public sealed record AnnualTotals(decimal GrossIncome, decimal DeductibleExpenses)
{
    public decimal NetIncome => GrossIncome - DeductibleExpenses;
}

/// <summary>
/// Profilul fiscal personal al anului (spec declarații §4): răspunsurile din profilul fiscal (Da/Nu,
/// completate de PFA) și cele anuale pentru D212. Separat de PFA: aparține persoanei, pe an.
/// </summary>
public sealed record PersonalTaxProfile(
    int TaxYear,
    ProfileFlags Flags,
    bool? HasExternalIncome,
    bool SupplementCompleted,
    decimal? AnafPrefilledNetIncome,
    decimal? CarriedLossesFromHistory);

/// <summary>Intrarea motorului anual: REF final, recalculul din ledger, profilul și parametrii anului.</summary>
public sealed record AnnualTaxInput(
    int TaxYear,
    AnnualTotals? RefFinal,
    AnnualTotals Recomputed,
    PersonalTaxProfile? Profile,
    TaxYearParameters? Parameters,
    int FormYear);

/// <summary>Modelul D212 al anului (spec declarații §4 <c>AnnualTaxResult</c>), independent de formular.</summary>
public sealed record D212DataModel(
    int TaxYear,
    int FormYear,
    string? RuleVersion,
    decimal GrossIncome,
    decimal DeductibleExpenses,
    decimal NetIncome,
    decimal CarriedLosses,
    decimal CasBase,
    decimal CasDue,
    decimal CassBase,
    decimal CassDue,
    decimal CassDeductible,
    decimal IncomeTaxBase,
    decimal IncomeTaxDue,
    decimal LossCarriedForward);

/// <summary>Comparația cu precompletarea ANAF (F54).</summary>
public enum PrefillCheck
{
    NotAvailable = 0,
    Match = 1,
    NeedsReview = 2,
}

public sealed record D212Calculation(D212DataModel? Model, PrefillCheck Prefill);

/// <summary>
/// Motorul anual D212 (spec declarații F50–F54). Refolosește motorul de taxe estimate
/// (<see cref="TaxEngine2026"/>) pe netul real al anului, cu parametrii anului fiscal: aceleași
/// formule pentru CAS, CASS și impozit, alt input (REF final în loc de proiecție).
/// </summary>
public static class AnnualTaxEngine
{
    public static AnnualCalculation<D212Calculation> Calculate(AnnualTaxInput input, ITaxEngine engine)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(engine);
        var blockers = new List<string>();
        var review = new List<string>();

        // F50: brutul și deductibilul sunt cele din REF-ul final; o diferență față de ledger oprește D212.
        if (input.RefFinal is not { } final)
        {
            blockers.Add($"Registrul de evidență fiscală {input.TaxYear} nu e final: anul nu e închis.");
        }
        else if (final.GrossIncome != input.Recomputed.GrossIncome || final.DeductibleExpenses != input.Recomputed.DeductibleExpenses)
        {
            blockers.Add(
                $"Ledger-ul diferă de REF-ul final {input.TaxYear}: brut {AccountingJson.Amount(input.Recomputed.GrossIncome)} față de {AccountingJson.Amount(final.GrossIncome)} lei, " +
                $"deductibil {AccountingJson.Amount(input.Recomputed.DeductibleExpenses)} față de {AccountingJson.Amount(final.DeductibleExpenses)} lei.");
        }

        // F53: întrebarea despre alte venituri e obligatorie.
        PersonalTaxProfile? profile = input.Profile;
        if (profile is null)
        {
            blockers.Add($"Profilul fiscal {input.TaxYear} nu e completat.");
        }
        else if (profile.HasExternalIncome is null)
        {
            blockers.Add("Lipsește răspunsul despre alte venituri sau contribuții în afara RIDElance.");
        }
        else if (profile.HasExternalIncome == true && !profile.SupplementCompleted)
        {
            blockers.Add("Formularul suplimentar pentru veniturile din afara RIDElance nu e completat.");
        }

        if (input.Parameters is null)
        {
            blockers.Add($"Parametrii fiscali {input.TaxYear} nu sunt configurați.");
        }

        if (blockers.Count > 0 || input.RefFinal is not { } totals || profile is null)
        {
            return new(new D212Calculation(null, PrefillCheck.NotAvailable), blockers, review);
        }

        // F52: pierderile reportate din istoric (D212 anterioară) sau din preluarea clientului.
        ProfileFlags flags = profile.Flags;
        if (profile.CarriedLossesFromHistory is { } history)
        {
            flags = flags with { CarriedLosses = history > 0, CarriedLossesAmount = history > 0 ? history : null };
        }

        decimal net = totals.NetIncome;
        var projection = new IncomeProjection(net, null, [], net, null, 0, 0);
        TaxResult result = engine.Calculate(new TaxInput(projection, flags, 0, 0, 1), input.Parameters);
        ComponentResult cas = result.Components.Single(c => c.Component == TaxComponents.Cas);
        ComponentResult cass = result.Components.Single(c => c.Component == TaxComponents.Cass);
        ComponentResult tax = result.Components.Single(c => c.Component == TaxComponents.IncomeTax);

        foreach (ComponentResult component in new[] { cas, cass, tax }.Where(c => c.Status != TaxStatuses.Estimated))
        {
            review.Add(component.ReasonCode == TaxReasons.CarriedLosses
                ? "Pierderile reportate nu au istoric complet (D212 anterioară sau suma preluată)."
                : $"{component.Component}: de clarificat ({string.Join(", ", component.MissingInputs.DefaultIfEmpty(component.ReasonCode ?? component.Status))}).");
        }

        if (review.Count > 0)
        {
            return new(new D212Calculation(null, PrefillCheck.NotAvailable), blockers, review);
        }

        decimal losses = Breakdown(tax, "carriedLosses");
        decimal unused = Math.Max(0, (flags.CarriedLossesAmount ?? 0) - losses);
        var model = new D212DataModel(
            input.TaxYear,
            input.FormYear,
            result.RuleVersion,
            totals.GrossIncome,
            totals.DeductibleExpenses,
            net,
            losses,
            Breakdown(cas, "base"),
            cas.Amount ?? 0,
            Breakdown(cass, "base"),
            cass.Amount ?? 0,
            Breakdown(cass, "deductible"),
            Breakdown(tax, "base"),
            tax.Amount ?? 0,
            unused + Math.Max(0, -net));

        // F54: precompletarea ANAF e doar control.
        PrefillCheck prefill = PrefillCheck.NotAvailable;
        if (profile.AnafPrefilledNetIncome is { } prefilled)
        {
            prefill = prefilled == net ? PrefillCheck.Match : PrefillCheck.NeedsReview;
            if (prefill == PrefillCheck.NeedsReview)
            {
                review.Add($"Precompletarea ANAF are venitul net {AccountingJson.Amount(prefilled)} lei, calculul {AccountingJson.Amount(net)} lei.");
            }
        }

        return new(new D212Calculation(model, prefill), blockers, review);
    }

    private static decimal Breakdown(ComponentResult component, string key) =>
        component.Breakdown.TryGetValue(key, out object? value) && value is decimal amount ? amount : 0;
}

/// <summary>Un câmp al formularului D212, cu valoarea lui.</summary>
public sealed record D212Field(string Section, string Label, decimal Value);

/// <summary>
/// Adaptorul formularului D212 al unui an de depunere (spec declarații F55): modelul anului fiscal
/// se mapează pe formularul în vigoare la depunere. Veniturile 2026 se depun în 2027 → adaptorul 2027.
/// </summary>
public interface ID212FormAdapter
{
    /// <summary>Anul depunerii pentru care e valabil formularul.</summary>
    int FormYear { get; }

    string FormVersion { get; }

    IReadOnlyList<D212Field> Map(D212DataModel model);
}

/// <summary>
/// Formularul D212 depus în 2027 (venituri 2026). Depunerea e manuală (aplicația web ANAF sau PDF-ul
/// inteligent, Q4): adaptorul dă valorile pe secțiuni, contabilul le transcrie și încarcă recipisa.
/// </summary>
public sealed class D212Form2027 : ID212FormAdapter
{
    public int FormYear => 2027;

    public string FormVersion => "D212-2027";

    public IReadOnlyList<D212Field> Map(D212DataModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        const string Income = "Cap. I — Venitul realizat din activități independente";
        const string Contributions = "Cap. I — Contribuțiile sociale datorate";
        return
        [
            new(Income, "Venit brut", model.GrossIncome),
            new(Income, "Cheltuieli deductibile", model.DeductibleExpenses),
            new(Income, model.NetIncome >= 0 ? "Venit net anual" : "Pierdere fiscală anuală", Math.Abs(model.NetIncome)),
            new(Income, "Pierderi fiscale reportate compensate", model.CarriedLosses),
            new(Income, "Venit net anual impozabil", model.IncomeTaxBase),
            new(Income, "Impozit pe venit datorat", model.IncomeTaxDue),
            new(Contributions, "Baza de calcul CAS", model.CasBase),
            new(Contributions, "CAS datorată", model.CasDue),
            new(Contributions, "Baza de calcul CASS", model.CassBase),
            new(Contributions, "CASS datorată", model.CassDue),
        ];
    }
}

public static class D212Forms
{
    /// <summary>Toate adaptoarele cunoscute; unul nou se adaugă aici când ANAF publică formularul anului.</summary>
    public static readonly IReadOnlyList<ID212FormAdapter> All = [new D212Form2027()];

    /// <summary>F55: adaptorul anului depunerii (din termenul D212 al anului fiscal), sau <c>null</c>.</summary>
    public static ID212FormAdapter? For(int formYear, IEnumerable<ID212FormAdapter>? adapters = null) =>
        (adapters ?? All).FirstOrDefault(a => a.FormYear == formYear);
}

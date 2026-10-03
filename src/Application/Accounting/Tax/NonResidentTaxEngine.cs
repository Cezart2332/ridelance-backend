using Domain.Accounting;

namespace Application.Accounting.Tax;

/// <summary>Rezultatul unei decizii de impozit nerezident.</summary>
public sealed record NonResidentDecisionResult(
    Guid? CertificateProfileId,
    Guid? TreatyRuleId,
    Guid AppliedRuleId,
    decimal Rate,
    decimal TaxDue,
    string ObligationCode,
    NonResidentDecisionStatus Status,
    string Explanation);

/// <summary>
/// Motorul de impozit pe veniturile nerezidenților (spec declarații §5, F20–F25), funcție pură: pentru
/// o plată, regula se caută după furnizorul juridic (cod fiscal) + țară + tipul venitului + data plății.
/// Certificat de rezidență valabil la data plății și regulă de tratat → cota din tratat; altfel
/// regula de fallback a perioadei. Fără certificat sau cu o regulă neconfirmată, decizia cere
/// confirmarea Adminului (F23).
/// </summary>
public static class NonResidentTaxEngine
{
    /// <summary>
    /// Regulile pe furnizor juridic din registrul de furnizori, în forma <see cref="TaxRule"/>:
    /// codul fiscal e cheia, tratatul e baza legală, cota și confirmarea sunt ale furnizorului.
    /// </summary>
    public static IReadOnlyList<TaxRule> SupplierRules(IEnumerable<SupplierTaxProfile> suppliers) =>
        [.. suppliers
            .Where(supplier => supplier.D100Rate is not null && supplier.DeletedAtUtc is null)
            .Select(supplier => new TaxRule
            {
                Id = supplier.Id,
                RuleType = TaxRuleTypes.NonResidentRate,
                Jurisdiction = supplier.Country.ToUpperInvariant(),
                ValidFrom = supplier.ValidFrom,
                ValidTo = supplier.ValidTo,
                LegalBasis = supplier.Treaty ?? string.Empty,
                Rate = supplier.D100Rate,
                SupplierEntityKey = supplier.VatId.ToUpperInvariant(),
                IncomeType = supplier.IncomeType.ToUpperInvariant(),
                Confirmed = supplier.D100RateConfirmed,
            })];

    public static NonResidentDecisionResult Decide(NonResidentPayment payment, IReadOnlyList<SupplierTaxProfile> suppliers, TaxRuleSet rules)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(rules);
        DateOnly date = payment.PaymentDate;
        string taxId = payment.SupplierTaxId.ToUpperInvariant();
        string incomeType = payment.IncomeType.ToUpperInvariant();

        // F24: codul de obligație vine din configurare.
        TaxRule obligation = rules.Find(TaxRuleTypes.ObligationCode, "RO", date, new TaxRuleContext("D100", IncomeType: incomeType))
            ?? rules.Resolve(TaxRuleTypes.ObligationCode, "RO", date, new TaxRuleContext("D100"));

        SupplierTaxProfile? supplier = suppliers.FirstOrDefault(profile =>
            profile.DeletedAtUtc is null &&
            string.Equals(profile.VatId, taxId, StringComparison.OrdinalIgnoreCase) &&
            profile.ValidFrom <= date && (profile.ValidTo is null || date <= profile.ValidTo));
        bool certificate = supplier is { ResidenceCertValidFrom: { } from, ResidenceCertValidTo: { } to } && from <= date && date <= to;

        // F21: regula pe entitate + țară + tip venit + dată (niciodată pe brand).
        var supplierRules = new TaxRuleSet(SupplierRules(suppliers));
        TaxRule? treaty = supplierRules.Find(
            TaxRuleTypes.NonResidentRate, payment.SupplierCountry.ToUpperInvariant(), date, new TaxRuleContext(SupplierEntityKey: taxId, IncomeType: incomeType));
        string day = AccountingJson.Date(date);

        if (certificate && treaty is { Rate: { } treatyRate })
        {
            return Result(
                supplier!.Id, treaty.Id, treaty.Id, treatyRate, payment, obligation,
                treaty.Confirmed ? NonResidentDecisionStatus.Auto : NonResidentDecisionStatus.NeedsLegalConfirmation,
                treaty.Confirmed
                    ? $"{treaty.LegalBasis}: cota {treatyRate:0.##}%, certificat de rezidență valabil la {day}."
                    : $"Regula de tratat pentru {payment.SupplierLegalName} ({treatyRate:0.##}%) nu e confirmată.");
        }

        // F22: fără certificat sau fără regulă de tratat, regula de fallback a perioadei.
        TaxRule fallback = rules.Resolve(TaxRuleTypes.NonResidentRate, "RO", date, new TaxRuleContext(IncomeType: incomeType));
        decimal rate = fallback.Rate ?? throw new TaxRuleConfigurationException($"Regula de fallback {incomeType} nu are cotă.");
        string reason = certificate
            ? $"Nicio regulă de tratat pentru {payment.SupplierLegalName} ({taxId}) la {day}; cota de fallback {rate:0.##}%."
            : $"Certificatul de rezidență pentru {payment.SupplierLegalName} nu e valabil la {day}; cota de fallback {rate:0.##}%.";

        // F23: lipsa certificatului sau o regulă neconfirmată cer confirmarea Adminului.
        NonResidentDecisionStatus status = certificate && fallback.Confirmed ? NonResidentDecisionStatus.Auto : NonResidentDecisionStatus.NeedsLegalConfirmation;
        return Result(certificate ? supplier!.Id : null, null, fallback.Id, rate, payment, obligation, status, reason);
    }

    private static NonResidentDecisionResult Result(
        Guid? certificate, Guid? treaty, Guid applied, decimal rate, NonResidentPayment payment, TaxRule obligation, NonResidentDecisionStatus status, string explanation) =>
        new(
            certificate,
            treaty,
            applied,
            rate,
            LedgerRound(payment.GrossIncomeRon * rate / 100),
            obligation.Formula ?? throw new TaxRuleConfigurationException("Regula codului de obligație D100 nu are cod."),
            status,
            explanation);

    private static decimal LedgerRound(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

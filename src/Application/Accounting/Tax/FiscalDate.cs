namespace Application.Accounting.Tax;

/// <summary>
/// Data care decide luna fiscală a unei facturi de comision și cota de TVA aplicabilă, după regula
/// de exigibilitate din configurare (DE CONFIRMAT).
/// </summary>
public static class FiscalDate
{
    public static DateOnly? Of(VatExigibilityRule rule, DateOnly? invoiceDate, DateOnly? servicePeriodEnd, DateOnly? taxPointDate) => rule switch
    {
        VatExigibilityRule.TaxPointDate => taxPointDate ?? servicePeriodEnd ?? invoiceDate,
        VatExigibilityRule.ServicePeriodEnd => servicePeriodEnd ?? invoiceDate,
        _ => invoiceDate,
    };

    /// <summary>Cum se numește data în mesaje.</summary>
    public static string Label(VatExigibilityRule rule, DateOnly? taxPointDate, DateOnly? servicePeriodEnd) => rule switch
    {
        VatExigibilityRule.TaxPointDate when taxPointDate is not null => "Data impozitării",
        VatExigibilityRule.TaxPointDate or VatExigibilityRule.ServicePeriodEnd when servicePeriodEnd is not null => "Sfârșitul perioadei facturate",
        _ => "Data facturii",
    };
}

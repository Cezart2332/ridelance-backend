namespace Application.PfaDashboard;

/// <summary>
/// Cotele fiscale folosite de cardul „Cât trebuie să pui deoparte". Stau în configurare,
/// nu în cod, ca modificarea unei cote să nu ceară un deploy de frontend — UI-ul primește
/// întotdeauna cota alături de sumă și o afișează ca atare.
/// </summary>
public sealed class FiscalPolicyOptions
{
    public const string SectionName = "Fiscal";

    /// <summary>TVA intracomunitar pe serviciile facturate de platforme (taxare inversă).</summary>
    public decimal VatIntracomRate { get; set; } = 0.21m;

    /// <summary>Impozit reținut pentru comisionul plătit unui nerezident (Bolt).</summary>
    public decimal BoltNonResidentRate { get; set; } = 0.02m;
}

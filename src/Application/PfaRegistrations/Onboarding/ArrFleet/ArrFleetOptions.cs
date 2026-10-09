namespace Application.PfaRegistrations.Onboarding.ArrFleet;

/// <summary>
/// Contul în care clientul plătește pasul „ARR &amp; Cont Flotă”. Din configurare
/// (<c>ArrFleet__Payment*</c>), nu din cod: contul se poate schimba fără deploy.
/// </summary>
public sealed class ArrFleetOptions
{
    public const string SectionName = "ArrFleet";

    public string? PaymentBeneficiary { get; set; }
    public string? PaymentIban { get; set; }
    public string? PaymentBank { get; set; }
}

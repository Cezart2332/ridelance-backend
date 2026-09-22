namespace Domain.FiscalProfiles;

/// <summary>
/// O reamintire săptămânală trimisă cât timp profilul fiscal nu e completat. Unicitatea pe
/// (PFA, an, săptămână) e garanția că nimeni nu primește două în aceeași săptămână.
/// </summary>
public sealed class FiscalProfileReminder
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int TaxYear { get; set; }
    public int WeekIndex { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
}

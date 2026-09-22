namespace Domain.FiscalProfiles;

/// <summary>
/// O sarcină de apel pentru echipă: la 30 de zile de la acces, un PFA fără profil fiscal
/// completat e sunat de un om. Una singură per (PFA, an, motiv).
/// </summary>
public sealed class AdminCallTask
{
    public const string ProfileIncomplete30Days = "PROFILE_INCOMPLETE_30D";

    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int TaxYear { get; set; }
    public string Reason { get; set; } = ProfileIncomplete30Days;
    public AdminCallTaskState State { get; set; } = AdminCallTaskState.Open;
    public Guid? OwnerUserId { get; set; }
    public string? CallOutcome { get; set; }
    public DateTime? RescheduledToUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
}

public enum AdminCallTaskState
{
    Open = 0,
    Done = 1,
    Rescheduled = 2,
    ResolvedByCompletion = 3
}

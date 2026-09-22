namespace Domain.FiscalProfiles;

/// <summary>
/// PFA-ul spune că o dată preluată din contul lui e greșită. Sursa nu se suprascrie: cererea
/// ajunge la echipă, care corectează la sursă și o închide.
/// </summary>
public sealed class PfaDataCorrectionRequest
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public Guid? ProfileId { get; set; }
    public string Fields { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DataCorrectionState State { get; set; } = DataCorrectionState.Open;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

public enum DataCorrectionState
{
    Open = 0,
    Resolved = 1
}

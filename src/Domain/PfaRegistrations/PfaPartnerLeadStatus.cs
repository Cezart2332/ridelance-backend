namespace Domain.PfaRegistrations;

/// <summary>
/// Statusul unui lead trimis către partenerul de înființare PFA (Consulto).
/// Avansul între statusuri se face manual din admin până apar integrările cu partenerul.
/// </summary>
public enum PfaPartnerLeadStatus
{
    RequestSent = 0,
    Contacted = 1,
    InProgress = 2,
    PfaCreated = 3,
    Cancelled = 4,
}

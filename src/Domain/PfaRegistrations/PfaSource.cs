namespace Domain.PfaRegistrations;

/// <summary>Cum a ajuns clientul să aibă PFA (Pasul 1).</summary>
public enum PfaSource
{
    /// <summary>Avea deja PFA („Am PFA").</summary>
    Existing = 0,

    /// <summary>PFA înființat printr-un partener (Consulto — „Nu am PFA").</summary>
    ViaPartner = 1,
}

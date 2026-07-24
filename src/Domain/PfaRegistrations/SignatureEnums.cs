namespace Domain.PfaRegistrations;

/// <summary>Cine gestionează semnarea pachetului de împuterniciri (Pasul 2.2).</summary>
public enum SignatureProvider
{
    /// <summary>Semnătură electronică prin EasyStream / Trans Sped.</summary>
    EasyStreamTransSped = 0,
    /// <summary>Semnătură olografă / procedură manuală.</summary>
    Manual = 1,
}

/// <summary>Starea pachetului de documente de semnat. Avans manual din admin până apar integrările.</summary>
public enum SignaturePacketStatus
{
    Draft = 0,
    Sent = 1,
    Completed = 2,
    Rejected = 3,
}

/// <summary>Tipurile de documente din pachetul de semnături, conform specificației.</summary>
public enum SignatureDocumentType
{
    PowerOfAttorneyArr = 0,
    PowerOfAttorneyAnaf = 1,
    ServiceContract = 2,
    GdprConsent = 3,
    Other = 99,
}

namespace Domain.Rentals;

/// <summary>
/// Ce se întâmplă cu chiria cât mașina stă în service fără vina chiriașului (clauza 5.4).
/// </summary>
public enum RentalUnavailabilityRule
{
    /// <summary>„… chiria pentru perioada de indisponibilitate nu se datorează.”</summary>
    NotDue,

    /// <summary>„… se reduce proporțional cu durata indisponibilității.”</summary>
    Proportional,

    /// <summary>„… se datorează integral.”</summary>
    FullyDue,
}

/// <summary>
/// Documentele mașinii care se predau odată cu ea. Codurile sunt ale API-ului; textul de pe
/// procesul-verbal îl pune generatorul.
/// </summary>
/// <remarks>
/// Listă de coduri, nu de texte: „talon”, „certificat” și „CI auto” sunt același act, iar
/// procesul-verbal trebuie să bifeze aceeași căsuță oricum l-ar fi numit cine a completat.
/// </remarks>
public static class HandoverDocumentCodes
{
    /// <summary>Certificatul de înmatriculare (talonul).</summary>
    public const string RegistrationCertificate = "RegistrationCertificate";

    /// <summary>Polița RCA.</summary>
    public const string Rca = "Rca";

    public static bool Contains(IEnumerable<string>? codes, string code) =>
        codes?.Any(c => string.Equals(c?.Trim(), code, StringComparison.OrdinalIgnoreCase)) == true;
}

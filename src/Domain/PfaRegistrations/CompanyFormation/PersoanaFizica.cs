namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Datele de identitate ale unei persoane fizice din dosarul de înființare — solicitantul sau
/// un proprietar al imobilului declarat ca sediu. Aceleași câmpuri în ambele cazuri, deci
/// aceeași definiție: un owned type aplatizat în entitatea gazdă.
/// </summary>
public sealed class PersoanaFizica
{
    public string? Nume { get; set; }
    public string? Prenume { get; set; }

    /// <summary>CNP-ul complet, criptat cu ISecretProtector. Niciodată în clar în DB sau în loguri.</summary>
    public string? CnpEncrypted { get; set; }

    /// <summary>CNP mascat pentru afișare (ex. „1******123456").</summary>
    public string? CnpMasked { get; set; }

    public TipActIdentitate TipAct { get; set; } = TipActIdentitate.CI;
    public string? SerieAct { get; set; }
    public string? NumarAct { get; set; }
    public string? AutoritateEmitenta { get; set; }
    public DateOnly? DataEmiterii { get; set; }
    public DateOnly? DataExpirarii { get; set; }

    public Adresa Domiciliu { get; set; } = new();

    /// <summary>Seria actului se cere doar la actele românești clasice.</summary>
    public bool RequiresSerie => TipAct is TipActIdentitate.CI or TipActIdentitate.BI;

    /// <summary>
    /// Toate câmpurile obligatorii sunt completate. Nu verifică validitatea CNP-ului
    /// (vezi <see cref="CnpValidator"/>) — doar prezența datelor.
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Nume)
        && !string.IsNullOrWhiteSpace(Prenume)
        && !string.IsNullOrWhiteSpace(CnpEncrypted)
        && (!RequiresSerie || !string.IsNullOrWhiteSpace(SerieAct))
        && !string.IsNullOrWhiteSpace(NumarAct)
        && !string.IsNullOrWhiteSpace(AutoritateEmitenta)
        && DataEmiterii is not null
        && DataExpirarii is not null
        && Domiciliu.IsComplete;
}

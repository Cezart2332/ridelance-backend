namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Adresă din România, folosită în trei locuri din dosarul de înființare: domiciliul
/// solicitantului, sediul social și domiciliul fiecărui proprietar. Owned type — nu are
/// tabel propriu, se aplatizează în entitatea gazdă cu prefix de coloană.
/// </summary>
public sealed class Adresa
{
    public string? Judet { get; set; }
    public string? Localitate { get; set; }
    public string? Strada { get; set; }

    /// <summary>Numărul poștal. Text, nu număr: acceptă „12A" și „FN" (fără număr).</summary>
    public string? Numar { get; set; }

    public string? Bloc { get; set; }
    public string? Scara { get; set; }
    public string? Etaj { get; set; }

    /// <summary>
    /// Nu e cerut explicit în specificație, dar apare pe CI și e necesar în actele
    /// depuse la registrul comerțului.
    /// </summary>
    public string? Apartament { get; set; }

    /// <summary>Adresa are completate toate câmpurile obligatorii.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Judet)
        && !string.IsNullOrWhiteSpace(Localitate)
        && !string.IsNullOrWhiteSpace(Strada)
        && !string.IsNullOrWhiteSpace(Numar);
}

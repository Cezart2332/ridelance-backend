namespace Domain.Cars;

/// <summary>
/// O deschidere a paginii de detaliu a unui anunț.
///
/// Rândul individual există ca să se poată răspunde la „câte vizualizări am avut săptămâna asta",
/// lucru pe care un contor simplu nu-l poate spune. Vizitatorul e reprezentat printr-un hash
/// (IP + user-agent + salt): IP-ul brut nu se stochează nicăieri, dar același vizitator poate fi
/// recunoscut suficient cât să nu numere de zece ori la fiecare refresh.
/// </summary>
public sealed class CarView
{
    public Guid Id { get; set; }
    public Guid CarId { get; set; }

    /// <summary>SHA-256 hex, 64 de caractere. Vezi <c>VisitorFingerprint</c>.</summary>
    public string VisitorHash { get; set; } = string.Empty;

    /// <summary>De unde a venit vizualizarea. Deocamdată doar „vdp”.</summary>
    public string Source { get; set; } = "vdp";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Car Car { get; set; } = null!;
}

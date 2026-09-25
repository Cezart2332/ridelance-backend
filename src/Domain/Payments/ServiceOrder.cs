namespace Domain.Payments;

/// <summary>
/// O comandă de serviciu individual (Înființare PFA, Găzduire sediu social, Start Ride), plătită
/// fără abonament — de pe site, fără cont, sau din dashboard.
///
/// Serviciile de înființare cer aceleași date ca ramura „Nu am PFA" din onboarding; ele stau în
/// <see cref="DossierJson"/>, nu într-un dosar de onboarding, pentru că cine cumpără de pe site
/// nu are cont și nici dosar PFA de care să se lege.
/// </summary>
public sealed class ServiceOrder
{
    public Guid Id { get; set; }
    public string ServiceKey { get; set; } = string.Empty;
    public string ServiceTitle { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public ServiceOrderStatus Status { get; set; } = ServiceOrderStatus.Pending;
    public string? StripeSessionId { get; set; }
    public long? AmountBani { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAtUtc { get; set; }

    /// <summary>Contul care a comandat din dashboard. Null pentru comenzile de pe site.</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Datele dosarului, ca JSON: solicitantul (CNP criptat), sediul, acordurile semnate,
    /// probatoriul semnăturii și fișierele criptate. Forma e a Application-ului.
    /// </summary>
    public string? DossierJson { get; set; }

    /// <summary>Când a plecat arhiva spre Consulto. Setat o singură dată — oprește retrimiterea.</summary>
    public DateTime? SentToConsultoAtUtc { get; set; }
}

public enum ServiceOrderStatus
{
    Pending,
    Paid,
}

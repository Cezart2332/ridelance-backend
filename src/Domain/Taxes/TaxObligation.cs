using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using SharedKernel;

namespace Domain.Taxes;

/// <summary>Ce declarație e. `Altele` acoperă ce apare fără să merite un tip propriu.</summary>
public enum TaxObligationType
{
    TvaIntracomunitar = 0,
    TaxaNerezident = 1,
    Altele = 2,
}

/// <summary>
/// Unde a ajuns declarația. Ordinea e cea reală: contabila o pregătește, o depune, suma devine
/// de plată, apoi e plătită.
/// </summary>
public enum TaxObligationStatus
{
    InPregatire = 0,
    Depusa = 1,
    DePlata = 2,
    Platita = 3,
}

/// <summary>
/// O obligație fiscală reală, stabilită și depusă de contabilă — nu o estimare a platformei.
///
/// Distincția e întregul rost al entității: cifrele calculate de RIDElance sunt orientative și
/// se schimbă cu fiecare cursă, pe când asta e o sumă pe care cineva a declarat-o la ANAF și
/// pe care utilizatorul chiar trebuie să o plătească până la o dată anume. Cele două nu apar
/// niciodată în aceeași listă (spec §7.3).
/// </summary>
public sealed class TaxObligation : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public TaxObligationType Type { get; set; }

    /// <summary>Perioada la care se referă declarația, nu cea în care a fost depusă.</summary>
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }

    public decimal AmountDue { get; set; }
    public DateOnly DueDate { get; set; }
    public TaxObligationStatus Status { get; set; } = TaxObligationStatus.InPregatire;

    /// <summary>Declarația, recipisa sau ordinul de plată, dacă au fost încărcate.</summary>
    public Guid? DocumentId { get; set; }

    public string? Note { get; set; }

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public PfaRegistration PfaRegistration { get; set; } = null!;
    public Document? Document { get; set; }
    public User CreatedByUser { get; set; } = null!;

    /// <summary>
    /// Termen depășit: are sens doar cât timp suma nu e plătită. O declarație plătită târziu
    /// nu mai e o problemă deschisă, deci nu se mai marchează.
    /// </summary>
    public bool IsOverdue(DateOnly today) =>
        Status != TaxObligationStatus.Platita && DueDate < today;
}

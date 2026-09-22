using Domain.PfaRegistrations;
using SharedKernel;

namespace Domain.FiscalProfiles;

/// <summary>
/// Profilul fiscal anual al unui PFA: situația personală din care se estimează taxele (contract de
/// muncă, pensie, alte venituri). Unul per PFA și an fiscal.
/// </summary>
/// <remarks>
/// Nu e același lucru cu <see cref="PfaFiscalProfile"/>, care ține setările de onboarding (TVA,
/// platforme, mașină) și nu depinde de an. Profilul de aici e opțional: aplicația merge fără el,
/// doar că estimările de taxe rămân ascunse până îl confirmă PFA-ul.
/// </remarks>
public sealed class PfaTaxProfile : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public int TaxYear { get; set; }

    /// <summary>Numai sistem real. Nu se întreabă „normă de venit sau sistem real”.</summary>
    public string Regime { get; set; } = "REAL";

    public PfaTaxProfileStatus Status { get; set; } = PfaTaxProfileStatus.NotStarted;

    /// <summary>Răspunsurile, cu exact cheile din formular. Tipate în Application.</summary>
    public string AnswersJson { get; set; } = "{}";

    /// <summary>Crește la fiecare salvare. E și ETag-ul pentru concurență.</summary>
    public int Revision { get; set; }

    /// <summary>Modalul automat de la prima accesare s-a arătat deja — nu mai reapare.</summary>
    public DateTime? FirstPromptShownAtUtc { get; set; }

    /// <summary>De când are PFA-ul acces la RIDElance: punctul de start al reamintirilor.</summary>
    public DateTime? AccessGrantedAtUtc { get; set; }

    // Data înființării, precompletată la crearea profilului. Sursa rămâne neatinsă: dacă PFA-ul
    // spune că e greșită, se deschide o cerere de corectare.
    public DateOnly? PfaRegisteredOn { get; set; }
    public string? PfaRegisteredOnSource { get; set; }
    public DateTime? PfaRegisteredOnObservedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? EstimatedTaxesUnlockedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? LastChangedByUserId { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
    public List<PfaTaxProfileRevision> Revisions { get; set; } = [];
}

public enum PfaTaxProfileStatus
{
    NotStarted = 0,
    Draft = 1,
    Completed = 2
}

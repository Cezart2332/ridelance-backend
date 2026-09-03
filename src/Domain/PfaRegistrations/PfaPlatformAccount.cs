using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaPlatformAccount : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public PfaPlatformProvider Provider { get; set; }
    public PfaPlatformAccountKind Kind { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? FullName { get; set; }
    public PfaFleetAccountStatus Status { get; set; } = PfaFleetAccountStatus.NotConfigured;

    // Pasul 4 — onboarding conturi operator (Uber/Bolt). Fără parole.
    /// <summary>Userul a selectat această platformă în onboarding.</summary>
    public bool IsSelectedByUser { get; set; }
    /// <summary>Userul are deja cont de operator pe platformă.</summary>
    public bool HasExistingAccount { get; set; }
    /// <summary>Răspunsul „ai deja cont?": HasOperatorAccount | None | DriverOnly | Unknown.</summary>
    public string? ExistingAccountAnswer { get; set; }
    /// <summary>Identificatorul contului de operator (nu parolă).</summary>
    public string? OperatorAccountId { get; set; }

    /// <summary>
    /// Parola contului de flotă, criptată cu <c>ISecretProtector</c> — ca IBAN-ul și CNP-ul.
    /// Nu iese niciodată înapoi spre client și nu apare în loguri: API-ul raportează doar
    /// <c>HasPassword</c>. Când șoferul nu are încă cont, e parola pe care o vrea la creare.
    /// </summary>
    public string? PasswordProtected { get; set; }

    public DateTime? PasswordUpdatedAtUtc { get; set; }

    // Contul de ȘOFER de pe aceeași platformă. Sunt două conturi distincte: cel de flotă
    // (operator) administrează mașinile, cel de șofer e cel cu care se conduce efectiv. Pasul
    // cerea doar flota, deci jumătate din ce trebuie ca să poți lucra lipsea din dosar.
    /// <summary>Emailul contului de șofer.</summary>
    public string? DriverEmail { get; set; }
    /// <summary>Telefonul contului de șofer, în format E.164.</summary>
    public string? DriverPhone { get; set; }
    /// <summary>Numele de pe contul de șofer, așa cum îl are platforma.</summary>
    public string? DriverFullName { get; set; }
    /// <summary>
    /// ID/UUID-ul de șofer de pe platformă. Nu se mai cere în onboarding — șoferii nu-l știu, iar
    /// platforma regăsește contul după email. Coloana rămâne pentru dosarele care îl au deja.
    /// </summary>
    public string? DriverExternalId { get; set; }

    /// <summary>Contractul de afiliere semnat cu platforma.</summary>
    public Guid? AffiliationContractDocumentId { get; set; }
    public PfaPlatformOnboardingStatus OnboardingStatus { get; set; } = PfaPlatformOnboardingStatus.NotStarted;
    public DateTime? ConfiguredAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
}

using Domain.Users;
using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>
/// Pasul 0 — eligibilitate. Se completează înainte de a exista un dosar PFA, deci se leagă
/// de user, nu de PfaRegistration. Datele provin din CI, permis și atestat (OCR + confirmare).
/// IMPORTANT: NU se stochează CNP — doar <see cref="DateOfBirth"/> derivată și o mască a seriei/numărului.
/// </summary>
public sealed class OnboardingEligibilityProfile : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    // Din CI
    public DateOnly? DateOfBirth { get; set; }
    /// <summary>Serie/număr act mascat (ex. "RX ***** 1234") — niciodată în clar.</summary>
    public string? IdSeriesMask { get; set; }
    public Guid? IdDocumentId { get; set; }

    // Din permis
    public DateOnly? CategoryBObtainedOn { get; set; }
    public string? DrivingCategories { get; set; }
    public DateOnly? DrivingLicenceExpiresOn { get; set; }
    public Guid? DrivingLicenceDocumentId { get; set; }

    // Atestat transport alternativ
    public bool HasDriverCertificate { get; set; }
    public DateOnly? DriverCertificateExpiresOn { get; set; }
    public Guid? DriverCertificateDocumentId { get; set; }

    public EligibilityStatus Status { get; set; } = EligibilityStatus.Pending;
    public string? StatusReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}

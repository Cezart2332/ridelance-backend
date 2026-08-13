using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>
/// Pasul 2.2 — pachet unic de împuterniciri/contracte de semnat. Un singur pachet per dosar,
/// cu mai multe documente. Avansul statusului se face manual din admin până apare integrarea
/// cu providerul de semnătură.
/// </summary>
public sealed class OnboardingSignaturePacket : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    public SignatureProvider Provider { get; set; } = SignatureProvider.EasyStreamTransSped;
    public SignaturePacketStatus Status { get; set; } = SignaturePacketStatus.Draft;

    public string? ProviderReference { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? SignedAtUtc { get; set; }

    /// <summary>Note interne, vizibile doar adminului.</summary>
    public string? AdminNote { get; set; }

    /// <summary>
    /// Momentul în care șoferul a apăsat „Trimite pentru verificare”, adică și-a terminat partea
    /// lui din pasul fiscal. Cât timp e setat și pachetul nu e nici finalizat, nici respins, pasul
    /// stă în <c>pending_admin</c>: nu mai are ce face userul, urmează alocarea pachetului.
    /// </summary>
    public DateTime? SubmittedForReviewAtUtc { get; set; }

    /// <summary>Pachetul alocat de admin (denumirea comercială/internă a setului de documente).</summary>
    public string? PackageName { get; set; }

    /// <summary>Câte semnături conține pachetul alocat.</summary>
    public int? SignatureCount { get; set; }

    /// <summary>Expirarea pachetului alocat.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Motivul respingerii, arătat șoferului. Separat de <see cref="AdminNote"/> intenționat:
    /// unul e pentru client, celălalt rămâne intern.
    /// </summary>
    public string? RejectionReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public List<OnboardingSignatureDocument> Documents { get; set; } = [];
}

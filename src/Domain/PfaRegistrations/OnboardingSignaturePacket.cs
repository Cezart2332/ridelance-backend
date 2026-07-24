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
    public string? AdminNote { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public List<OnboardingSignatureDocument> Documents { get; set; } = [];
}

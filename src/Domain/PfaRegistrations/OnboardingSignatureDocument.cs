using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>Un document din pachetul de semnături (Pasul 2.2).</summary>
public sealed class OnboardingSignatureDocument : Entity
{
    public Guid Id { get; set; }
    public Guid PacketId { get; set; }

    public SignatureDocumentType Type { get; set; }
    public string? Label { get; set; }

    /// <summary>Documentul semnat încărcat (dacă e cazul).</summary>
    public Guid? DocumentId { get; set; }
    public bool IsSigned { get; set; }
    public DateTime? SignedAtUtc { get; set; }

    // Navigation
    public OnboardingSignaturePacket Packet { get; set; } = null!;
}

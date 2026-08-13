using Domain.Users;
using SharedKernel;

namespace Domain.PfaRegistrations;

/// <summary>
/// Urma tranzițiilor de pas: cine a mutat un pas dintr-o stare în alta și când.
///
/// Statusul pașilor se derivă, nu se stochează, deci fără rândurile astea nu ar exista nicio
/// dovadă că un pas a fost finalizat sau respins de un anume om. <see cref="PfaActivityLog"/> nu
/// acoperă cazul: e text liber, deci nu se poate interoga „ce s-a întâmplat pe pasul fiscal”.
/// </summary>
public sealed class OnboardingStepAudit : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary>Cheia pasului, exact cea trimisă clientului (<c>fiscal</c>, <c>arr</c>, …).</summary>
    public string StepKey { get; set; } = string.Empty;

    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;

    /// <summary>Null pentru tranzițiile declanșate de șofer prin fluxul normal.</summary>
    public Guid? PerformedByUserId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public User? PerformedByUser { get; set; }
}

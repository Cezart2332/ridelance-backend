using Domain.PfaRegistrations;
using Domain.Users;
using SharedKernel;

namespace Domain.FiscalLink;

/// <summary>
/// Legătura dintre un PFA și clientul lui la FiscalLink. Clientul se creează o singură dată; codul
/// de activare și casele de marcat se citesc de la FiscalLink de fiecare dată, nu se copiază aici.
/// </summary>
public sealed class FiscalLinkClient : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary>ID-ul clientului la FiscalLink (<c>clients/{id}</c>).</summary>
    public Guid FiscalLinkClientId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public User User { get; set; } = null!;
    public PfaRegistration PfaRegistration { get; set; } = null!;
}

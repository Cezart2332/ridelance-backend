using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaFleetConsent : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public bool FleetAccountsAccepted { get; set; }
    public DateTime? FleetAccountsAcceptedAtUtc { get; set; }
    public bool BoltApiAccepted { get; set; }
    public DateTime? BoltApiAcceptedAtUtc { get; set; }
    public string ConsentTextVersion { get; set; } = "2026-06";
    public Guid? AcceptedByUserId { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
}

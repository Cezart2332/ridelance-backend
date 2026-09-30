using Domain.PfaRegistrations;
using Domain.Users;
using SharedKernel;

namespace Domain.Uber;

/// <summary>
/// O cursă din raportul Uber de curse („Istoric curse”). Totalurile lunii rămân pe
/// <see cref="UberCsvImport"/>; aici stă fiecare rând, ca să poată fi listat în istoricul
/// curselor lângă cele Bolt. Raportul nu are câștigul pe cursă — Uber îl dă doar pe lună.
/// </summary>
public sealed class UberTrip : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PfaRegistrationId { get; set; }

    /// <summary>Importul din care a venit rândul. Ștergerea importului îl ia cu el.</summary>
    public Guid UberCsvImportId { get; set; }

    /// <summary>UUID-ul cursei la Uber. Unic pe PFA: același rând importat de două ori e o singură cursă.</summary>
    public string TripUuid { get; set; } = string.Empty;

    public DateTime RequestedAtUtc { get; set; }
    public DateTime? DroppedOffAtUtc { get; set; }
    public string PickupAddress { get; set; } = string.Empty;
    public string DestinationAddress { get; set; } = string.Empty;
    public double DistanceKm { get; set; }

    /// <summary>Starea la Uber: <c>completed</c>, <c>rider_cancelled</c> etc.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Tipul produsului: Comfort, Black, UberX…</summary>
    public string ProductType { get; set; } = string.Empty;

    /// <summary>Tipul plății, cum îl scrie Uber: <c>cash</c>, <c>braintree</c>, <c>apple_pay</c>…</summary>
    public string PaymentType { get; set; } = string.Empty;

    public User User { get; set; } = null!;
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public UberCsvImport UberCsvImport { get; set; } = null!;
}

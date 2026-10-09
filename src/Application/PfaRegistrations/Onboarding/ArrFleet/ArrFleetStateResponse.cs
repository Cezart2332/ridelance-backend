namespace Application.PfaRegistrations.Onboarding.ArrFleet;

/// <summary>
/// Starea pasului „ARR &amp; Cont Flotă”. Aceeași formă pentru client și admin: adminul vede exact
/// ce a introdus clientul, din aceleași entități.
/// </summary>
public sealed record ArrFleetStateResponse(
    Guid PfaRegistrationId,
    string Status,
    string StatusLabel,
    IReadOnlyList<string> Platforms,
    IReadOnlyList<ArrFleetDriverAccountDto> DriverAccounts,
    string? VehicleOwnership,
    long PaymentAmountBani,
    string PaymentExplanation,
    ArrFleetPaymentDetailsDto PaymentDetails,
    bool PaymentProofOutdated,
    DateTime? SubmittedAtUtc,
    string? ReopenedReason,
    IReadOnlyList<string> Missing,
    IReadOnlyList<ArrFleetStatusLogDto> StatusLog);

/// <summary>
/// Contul de șofer pe o platformă aleasă. <c>HasAccount = false</c> înseamnă „nu am cont”: agentul
/// îl sună pe client (<c>RequiresPhoneCall</c>) și îi deschide contul.
/// </summary>
public sealed record ArrFleetDriverAccountDto(
    string Platform,
    bool? HasAccount,
    string? Email,
    string? Phone,
    string? FullName,
    bool RequiresPhoneCall);

public sealed record ArrFleetPaymentDetailsDto(string? Beneficiary, string? Iban, string? Bank);

public sealed record ArrFleetStatusLogDto(string FromStatus, string ToStatus, string? ChangedBy, DateTime ChangedAtUtc);

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
    IReadOnlyList<ArrFleetPaymentDto> Payments,
    ArrAgencyDto? Agency,
    string? AgencyError,
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

/// <summary>O plată separată: suma, explicația și categoria în care se încarcă dovada ei.</summary>
public sealed record ArrFleetPaymentDto(
    string Kind,
    string Label,
    string Explanation,
    long AmountBani,
    string ProofCategory,
    bool ProofUploaded);

/// <summary>Agenția teritorială ARR din județul sediului social, cu contul ei de trezorerie.</summary>
public sealed record ArrAgencyDto(
    string CountyCode,
    string CountyName,
    string BeneficiaryName,
    string Treasury,
    string FiscalCode,
    string Iban);

public sealed record ArrFleetStatusLogDto(string FromStatus, string ToStatus, string? ChangedBy, DateTime ChangedAtUtc);

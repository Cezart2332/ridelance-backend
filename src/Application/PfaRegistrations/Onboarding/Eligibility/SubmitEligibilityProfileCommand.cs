using Application.Abstractions.Messaging;

namespace Application.PfaRegistrations.Onboarding.Eligibility;

public sealed record SubmitEligibilityProfileCommand(
    Guid UserId,
    DateOnly? DateOfBirth,
    string? IdSeriesMask,
    DateOnly? CategoryBObtainedOn,
    string? DrivingCategories,
    DateOnly? DrivingLicenceExpiresOn,
    bool HasDriverCertificate,
    DateOnly? DriverCertificateExpiresOn) : ICommand<EligibilityProfileResponse>;

namespace Application.PfaRegistrations.Onboarding.Eligibility;

public sealed record EligibilityProfileResponse(
    Guid? Id,
    DateOnly? DateOfBirth,
    string? IdSeriesMask,
    DateOnly? CategoryBObtainedOn,
    string? DrivingCategories,
    DateOnly? DrivingLicenceExpiresOn,
    bool HasDriverCertificate,
    DateOnly? DriverCertificateExpiresOn,
    string Status,
    IReadOnlyList<string> Reasons);

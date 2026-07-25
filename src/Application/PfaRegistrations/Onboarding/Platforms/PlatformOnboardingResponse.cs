using Domain.PfaRegistrations;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Platforms;

public sealed record PlatformAccountDto(
    string Provider,
    bool IsSelectedByUser,
    bool HasExistingAccount,
    string? OperatorAccountId,
    bool HasAffiliationContract,
    string OnboardingStatus,
    string? ExistingAccountAnswer);

public sealed record PlatformOnboardingResponse(
    Guid? PfaRegistrationId,
    IReadOnlyList<PlatformAccountDto> Platforms);

internal static class PlatformShared
{
    public static readonly Error NoRegistration = Error.Problem(
        "Onboarding.Platforms.NoRegistration",
        "Nu există un dosar PFA pentru utilizatorul curent.");

    public static readonly Error AccountNotFound = Error.NotFound(
        "Onboarding.Platforms.NotFound",
        "Platforma cerută nu a fost selectată în onboarding.");

    /// <summary>Contul de onboarding al unei platforme e cel de tip Driver (contul propriu al șoferului).</summary>
    public static PfaPlatformAccount? DriverAccount(PfaRegistration registration, PfaPlatformProvider provider) =>
        registration.PlatformAccounts
            .SingleOrDefault(a => a.Provider == provider && a.Kind == PfaPlatformAccountKind.Driver);

    public static PlatformAccountDto ToDto(PfaPlatformAccount a) => new(
        a.Provider.ToString(),
        a.IsSelectedByUser,
        a.HasExistingAccount,
        a.OperatorAccountId,
        a.AffiliationContractDocumentId is not null,
        a.OnboardingStatus.ToString(),
        a.ExistingAccountAnswer);

    public static PlatformOnboardingResponse ToResponse(PfaRegistration registration)
    {
        var dtos = registration.PlatformAccounts
            .Where(a => a.Kind == PfaPlatformAccountKind.Driver)
            .OrderBy(a => a.Provider)
            .Select(ToDto)
            .ToList();

        return new PlatformOnboardingResponse(registration.Id, dtos);
    }
}

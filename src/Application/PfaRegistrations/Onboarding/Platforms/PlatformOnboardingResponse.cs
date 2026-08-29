using Domain.PfaRegistrations;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Platforms;

/// <param name="HasPassword">
/// Doar dacă există o parolă salvată. Valoarea nu iese niciodată din server — formularul o
/// re-cere doar dacă utilizatorul vrea s-o schimbe.
/// </param>
public sealed record PlatformAccountDto(
    string Provider,
    bool IsSelectedByUser,
    bool HasExistingAccount,
    string? OperatorAccountId,
    bool HasAffiliationContract,
    string OnboardingStatus,
    string? ExistingAccountAnswer,
    string? Email,
    string? Phone,
    bool HasPassword,
    string? DriverEmail,
    string? DriverPhone,
    string? DriverExternalId);

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

    public static readonly Error InvalidEmail = Error.Problem(
        "Onboarding.Platforms.InvalidEmail",
        "Adresa de email nu este validă.");

    public static readonly Error InvalidPhone = Error.Problem(
        "Onboarding.Platforms.InvalidPhone",
        "Numărul de telefon trebuie să fie în format internațional (ex. +40712345678).");

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
        a.ExistingAccountAnswer,
        a.Email,
        a.Phone,
        !string.IsNullOrWhiteSpace(a.PasswordProtected),
        a.DriverEmail,
        a.DriverPhone,
        a.DriverExternalId);

    public static PlatformOnboardingResponse ToResponse(PfaRegistration registration)
    {
        var dtos = registration.PlatformAccounts
            .Where(a => a.Kind == PfaPlatformAccountKind.Driver)
            .OrderBy(a => a.Provider)
            .Select(ToDto)
            .ToList();

        return new PlatformOnboardingResponse(registration.Id, dtos);
    }

    /// <summary>
    /// Partea pe care o poate face șoferul: a răspuns la „ai cont?", a lăsat datele contului de
    /// flotă (email, telefon, parolă) ȘI pe cele ale contului de șofer.
    ///
    /// Contul de șofer nu e opțional: fără el nu se poate conduce pe platformă, deci un pas
    /// „complet" doar cu flota ar declara gata un dosar cu care nu se poate lucra. ID-ul de
    /// șofer rămâne opțional — nu toți îl știu, iar platforma îl regăsește după email.
    /// </summary>
    public static bool UserPartComplete(PfaPlatformAccount account) =>
        !string.IsNullOrWhiteSpace(account.Email)
        && !string.IsNullOrWhiteSpace(account.Phone)
        && !string.IsNullOrWhiteSpace(account.PasswordProtected)
        && !string.IsNullOrWhiteSpace(account.ExistingAccountAnswer)
        && !string.IsNullOrWhiteSpace(account.DriverEmail)
        && !string.IsNullOrWhiteSpace(account.DriverPhone);
}

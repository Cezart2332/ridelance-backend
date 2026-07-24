using Domain.PfaRegistrations;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

internal static class ArrShared
{
    /// <summary>Cheia taxei ARR în app_settings; implicit 300 lei = 30000 bani.</summary>
    public const string FeeSettingKey = "fees.arr.authorization.bani";
    public const long DefaultFeeBani = 30_000;

    public static readonly Error NoRegistration = Error.Problem(
        "Onboarding.Arr.NoRegistration",
        "Nu există un dosar PFA pentru utilizatorul curent.");

    public static readonly Error NotFound = Error.NotFound(
        "Onboarding.Arr.NotFound",
        "Nu există o cerere de autorizație ARR pentru acest dosar.");

    public static ArrStateResponse ToResponse(ArrAuthorizationRequest a) => new(
        a.PfaRegistrationId,
        a.AgencyName,
        a.FeeSnapshotBani,
        a.SubmissionMethod.ToString(),
        a.Status.ToString(),
        a.DossierDocumentId is not null,
        a.DossierDocumentId,
        a.DossierGeneratedAtUtc,
        a.SubmittedAtUtc,
        a.AuthorizationDocumentId,
        a.AuthorizationNumber,
        a.AuthorizationIssuedOn,
        a.AuthorizationExpiresOn,
        a.AdminNote);
}

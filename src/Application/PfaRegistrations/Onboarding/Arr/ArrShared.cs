using Application.Abstractions.Data;
using Application.Abstractions.Settings;
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

    /// <summary>
    /// Numele solicitantului se tipărește pe dosarul depus la ARR. De la RL-05 contul poate exista
    /// fără nume (vine din buletin), așa că aici NU se cade pe email — se cere completarea.
    /// </summary>
    public static readonly Error ApplicantNameMissing = Error.Problem(
        "Onboarding.Arr.ApplicantNameMissing",
        "Completează numele și prenumele înainte de a genera dosarul — apar pe cererea depusă la ARR.");

    public static readonly Error NotFound = Error.NotFound(
        "Onboarding.Arr.NotFound",
        "Nu există o cerere de autorizație ARR pentru acest dosar.");

    /// <summary>
    /// Cererea ARR se creează de obicei la pasul „Unde depui dosarul?”, dar generarea dosarului
    /// nu trebuie blocată dacă userul a sărit acel ecran (rail, județ precompletat fără persist).
    /// </summary>
    public static async Task<ArrAuthorizationRequest> EnsureRequestAsync(
        IApplicationDbContext context,
        IAppSettings appSettings,
        PfaRegistration registration,
        CancellationToken cancellationToken)
    {
        if (registration.ArrAuthorizationRequest is not null)
        {
            return registration.ArrAuthorizationRequest;
        }

        DateTime nowUtc = DateTime.UtcNow;

        var request = new ArrAuthorizationRequest
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            AgencyName = registration.County,
            SubmissionMethod = ArrSubmissionMethod.InPersonByClient,
            FeeSnapshotBani = await appSettings.GetAsync(FeeSettingKey, DefaultFeeBani, cancellationToken),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };

        context.ArrAuthorizationRequests.Add(request);
        return request;
    }

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

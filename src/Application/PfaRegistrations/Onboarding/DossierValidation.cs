using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.Arr;
using Application.PfaRegistrations.Onboarding.Vehicle;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding;

/// <summary>Dosarul unui pas: <c>arr</c> (autorizația de transport) sau <c>vehicle</c> (copia conformă).</summary>
public static class DossierSteps
{
    public const string Arr = "arr";
    public const string Vehicle = "vehicle";
}

/// <summary>
/// Adminul validează actele care intră în dosar, înainte ca clientul să-l poată genera. E altceva
/// decât validarea pasului: pasul se validează abia la final, după ce clientul depune dosarul și
/// încarcă autorizația (sau copia conformă) primită.
/// </summary>
public sealed record ValidateDossierDocumentsCommand(Guid RegistrationId, string Step, Guid ReviewerUserId) : ICommand<DossierReadinessResponse>;

/// <summary>Cum stă dosarul unui pas, pentru admin.</summary>
public sealed record GetDossierReadinessQuery(Guid RegistrationId, string Step) : IQuery<DossierReadinessResponse>;

/// <param name="Missing">Acte pe care clientul nu le-a încărcat încă.</param>
/// <param name="AwaitingValidation">Acte încărcate, încă nevalidate de echipă.</param>
public sealed record DossierReadinessResponse(string Step, IReadOnlyList<string> Missing, IReadOnlyList<string> AwaitingValidation);

internal static class DossierValidation
{
    public static readonly Error UnknownStep =
        Error.Problem("Onboarding.Dossier.UnknownStep", "Pasul nu are dosar.");

    public static Error StillMissing(IReadOnlyList<string> labels) => Error.Problem(
        "Onboarding.Dossier.DocumentsMissing",
        $"Clientul n-a încărcat încă: {string.Join(", ", labels)}. Actele se validează după ce sunt toate încărcate.");

    public static async Task<(PfaRegistration? Registration, IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> Requirements)> LoadAsync(
        IApplicationDbContext context,
        Guid registrationId,
        string step,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<PfaRegistration> query = context.PfaRegistrations.Include(r => r.Vehicles);
        PfaRegistration? registration = await (tracking ? query : query.AsNoTracking())
            .SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken);

        if (registration is null)
        {
            return (null, []);
        }

        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements = step switch
        {
            DossierSteps.Arr => OnboardingSectionCatalog.RequirementsFor(OnboardingSectionKey.AutorizatieTransport),
            DossierSteps.Vehicle => OnboardingSectionCatalog.RequirementsForVehicleDossier(
                VehicleShared.PrimaryVehicle(registration)?.OwnershipMode ?? VehicleOwnershipMode.Owned),
            _ => [],
        };

        return (registration, requirements);
    }

    public static string DossierLabel(string step) =>
        step == DossierSteps.Arr ? "autorizația de transport alternativ" : "copia conformă și ecusoane";
}

internal sealed class GetDossierReadinessQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetDossierReadinessQuery, DossierReadinessResponse>
{
    public async Task<Result<DossierReadinessResponse>> Handle(GetDossierReadinessQuery query, CancellationToken cancellationToken)
    {
        if (query.Step is not (DossierSteps.Arr or DossierSteps.Vehicle))
        {
            return Result.Failure<DossierReadinessResponse>(DossierValidation.UnknownStep);
        }

        (PfaRegistration? registration, IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements) =
            await DossierValidation.LoadAsync(context, query.RegistrationId, query.Step, tracking: false, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<DossierReadinessResponse>(PfaRegistrationErrors.NotFound(query.RegistrationId));
        }

        DossierReadiness readiness = await DossierAttachments.ReadinessAsync(context, registration.UserId, requirements, cancellationToken);
        return new DossierReadinessResponse(query.Step, readiness.Missing, readiness.Unverified);
    }
}

internal sealed class ValidateDossierDocumentsCommandHandler(IApplicationDbContext context)
    : ICommandHandler<ValidateDossierDocumentsCommand, DossierReadinessResponse>
{
    public async Task<Result<DossierReadinessResponse>> Handle(ValidateDossierDocumentsCommand command, CancellationToken cancellationToken)
    {
        if (command.Step is not (DossierSteps.Arr or DossierSteps.Vehicle))
        {
            return Result.Failure<DossierReadinessResponse>(DossierValidation.UnknownStep);
        }

        (PfaRegistration? registration, IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> requirements) =
            await DossierValidation.LoadAsync(context, command.RegistrationId, command.Step, tracking: true, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<DossierReadinessResponse>(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        DossierReadiness before = await DossierAttachments.ReadinessAsync(context, registration.UserId, requirements, cancellationToken);
        if (before.Missing.Count > 0)
        {
            return Result.Failure<DossierReadinessResponse>(DossierValidation.StillMissing(before.Missing));
        }

        IReadOnlyList<string> validated = await DossierAttachments.VerifyLatestAsync(
            context, registration.UserId, requirements, cancellationToken);

        if (validated.Count > 0)
        {
            DateTime now = DateTime.UtcNow;
            string label = DossierValidation.DossierLabel(command.Step);
            context.PfaActivityLogs.Add(new PfaActivityLog
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                ActivityType = "DossierDocumentsValidated",
                Description = $"Actele pentru dosarul de {label} au fost validate: {string.Join(", ", validated)}.",
                CreatedAtUtc = now,
                PerformedByUserId = command.ReviewerUserId,
            });
            context.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = registration.UserId,
                Text = $"Actele pentru dosarul de {label} au fost validate. Poți genera dosarul.",
                Type = NotificationTypes.OnboardingSectionUpdate,
                SectionKey = command.Step,
                IsRead = false,
                CreatedAtUtc = now,
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        return new DossierReadinessResponse(command.Step, [], []);
    }
}

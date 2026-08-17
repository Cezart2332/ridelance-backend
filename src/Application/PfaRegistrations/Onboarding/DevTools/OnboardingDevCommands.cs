using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.DevTools;

/// <summary>
/// Uneltele de dezvoltare pentru onboarding (spec fix-uri §13.2).
///
/// Toate trei scriu în <see cref="OnboardingStepAudit"/>: cine, când, de la ce pas la ce pas.
/// Un salt fără urmă ar face imposibil de spus, mai târziu, dacă un dosar a fost parcurs sau
/// fabricat.
///
/// Autorizarea NU e aici — e în endpoint, prin <see cref="OnboardingDevToolsGate"/>, care
/// răspunde 404 când poarta nu trece. Comenzile presupun că apelul a trecut deja de poartă.
/// </summary>

/// <summary>Ce mai lipsește ca să existe pe ce lucra.</summary>
internal static class DevToolsErrors
{
    public static readonly Error NoRegistration = Error.NotFound(
        "Onboarding.DevTools.NoRegistration",
        "Nu există un dosar PFA pentru acest onboarding.");

    public static readonly Error UnknownStep = Error.Problem(
        "Onboarding.DevTools.UnknownStep",
        "Pasul cerut nu există.");
}

/// <summary>
/// Mută starea la pasul țintă FĂRĂ să ruleze validările pașilor anteriori — dar populându-i cu
/// fixtures, ca pasul țintă să aibă tot ce îi trebuie. Fără asta, testul n-ar fi relevant:
/// ecranele s-ar bloca pe date lipsă în loc să arate bugul căutat.
/// </summary>
public sealed record JumpToOnboardingStepCommand(Guid RegistrationId, Guid PerformedByUserId, string TargetStepKey)
    : ICommand;

internal sealed class JumpToOnboardingStepCommandHandler(IApplicationDbContext context)
    : ICommandHandler<JumpToOnboardingStepCommand>
{
    public async Task<Result> Handle(JumpToOnboardingStepCommand command, CancellationToken cancellationToken)
    {
        if (!OnboardingDevFixtures.TryParseKey(command.TargetStepKey, out OnboardingStepKey target))
        {
            return Result.Failure(DevToolsErrors.UnknownStep);
        }

        PfaRegistration? registration = await DevToolsQueries.LoadAsync(context, command.RegistrationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure(DevToolsErrors.NoRegistration);
        }

        OnboardingEligibilityProfile? eligibility = await context.OnboardingEligibilityProfiles
            .FirstOrDefaultAsync(e => e.UserId == registration.UserId, cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        // Toți pașii dinaintea țintei primesc fixture-ul lor. Ținta rămâne neatinsă — acolo
        // testerul vrea să lucreze el.
        foreach (OnboardingStepKey step in OnboardingDevFixtures.Order.TakeWhile(s => s < target))
        {
            OnboardingDevFixtures.Apply(context, registration, eligibility, step, nowUtc);
            eligibility ??= await context.OnboardingEligibilityProfiles
                .FirstOrDefaultAsync(e => e.UserId == registration.UserId, cancellationToken);

            DevToolsQueries.Audit(context, registration, step, "SkippedInDev", command.PerformedByUserId, nowUtc);
        }

        registration.IsDevSession = true;
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>Completează un singur pas cu fixture-ul lui și îl marchează terminat.</summary>
public sealed record CompleteOnboardingStepCommand(
    Guid RegistrationId,
    Guid PerformedByUserId,
    string StepKey,
    bool UseMockData) : ICommand;

internal sealed class CompleteOnboardingStepCommandHandler(IApplicationDbContext context)
    : ICommandHandler<CompleteOnboardingStepCommand>
{
    public async Task<Result> Handle(CompleteOnboardingStepCommand command, CancellationToken cancellationToken)
    {
        if (!OnboardingDevFixtures.TryParseKey(command.StepKey, out OnboardingStepKey step))
        {
            return Result.Failure(DevToolsErrors.UnknownStep);
        }

        PfaRegistration? registration = await DevToolsQueries.LoadAsync(context, command.RegistrationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure(DevToolsErrors.NoRegistration);
        }

        OnboardingEligibilityProfile? eligibility = await context.OnboardingEligibilityProfiles
            .FirstOrDefaultAsync(e => e.UserId == registration.UserId, cancellationToken);

        DateTime nowUtc = DateTime.UtcNow;

        // `useMockData: false` marchează pasul terminat fără să inventeze conținut — util când
        // datele reale sunt deja acolo și se testează doar tranziția.
        if (command.UseMockData)
        {
            OnboardingDevFixtures.Apply(context, registration, eligibility, step, nowUtc);
        }

        registration.IsDevSession = true;
        DevToolsQueries.Audit(context, registration, step, "CompletedInDev", command.PerformedByUserId, nowUtc);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Readuce onboardingul la zero, pe o felie sau pe tot. Nu șterge dosarul: îi scoate doar
/// entitățile derivate, ca pașii să se recalculeze de la început.
/// </summary>
public sealed record ResetOnboardingCommand(
    Guid RegistrationId,
    Guid PerformedByUserId,
    /// <summary>`step` | `section` | `all`.</summary>
    string Scope,
    string? TargetId) : ICommand;

internal sealed class ResetOnboardingCommandHandler(IApplicationDbContext context)
    : ICommandHandler<ResetOnboardingCommand>
{
    public async Task<Result> Handle(ResetOnboardingCommand command, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await DevToolsQueries.LoadAsync(context, command.RegistrationId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure(DevToolsErrors.NoRegistration);
        }

        DateTime nowUtc = DateTime.UtcNow;
        bool all = string.Equals(command.Scope, "all", StringComparison.OrdinalIgnoreCase);

        bool parsed = OnboardingDevFixtures.TryParseKey(command.TargetId, out OnboardingStepKey single);
        if (!all && !parsed)
        {
            return Result.Failure(DevToolsErrors.UnknownStep);
        }

        IReadOnlyList<OnboardingStepKey> steps = all ? OnboardingDevFixtures.Order : [single];

        foreach (OnboardingStepKey step in steps)
        {
            Reset(registration, step);
            DevToolsQueries.Audit(context, registration, step, "ResetInDev", command.PerformedByUserId, nowUtc);
        }

        registration.IsDevSession = true;
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Șterge entitățile din care se derivă statusul pasului. Documentele NU se ating: sunt
    /// fișiere reale încărcate de tester, iar re-încărcarea lor la fiecare reset ar face
    /// instrumentul inutilizabil.
    /// </summary>
    private void Reset(PfaRegistration registration, OnboardingStepKey step)
    {
        switch (step)
        {
            case OnboardingStepKey.Pfa:
                registration.Status = PfaRegistrationStatus.Pending;
                registration.ReviewedAtUtc = null;
                break;

            case OnboardingStepKey.Fiscal:
                if (registration.SignaturePacket is { } packet)
                {
                    context.OnboardingSignaturePackets.Remove(packet);
                    registration.SignaturePacket = null;
                }
                if (registration.OblioAccount is { } oblio)
                {
                    context.PfaOblioAccounts.Remove(oblio);
                    registration.OblioAccount = null;
                }
                if (registration.BankAccountDeclaration is { } bank)
                {
                    context.PfaBankAccountDeclarations.Remove(bank);
                    registration.BankAccountDeclaration = null;
                }
                break;

            case OnboardingStepKey.Arr:
                if (registration.ArrAuthorizationRequest is { } arr)
                {
                    context.ArrAuthorizationRequests.Remove(arr);
                    registration.ArrAuthorizationRequest = null;
                }
                break;

            case OnboardingStepKey.Platforms:
                context.PfaPlatformAccounts.RemoveRange(registration.PlatformAccounts);
                registration.PlatformAccounts.Clear();
                break;

            case OnboardingStepKey.Vehicle:
                foreach (PfaVehicle vehicle in registration.Vehicles)
                {
                    if (vehicle.CopyRequest is { } copy)
                    {
                        context.VehicleCopyRequests.Remove(copy);
                        vehicle.CopyRequest = null;
                    }
                }
                context.PfaVehicles.RemoveRange(registration.Vehicles);
                registration.Vehicles.Clear();
                break;

            case OnboardingStepKey.Eligibility:
            default:
                // Profilul de eligibilitate atârnă de user, nu de dosar: se resetează separat,
                // prin `scope: "all"` de pe contul respectiv.
                break;
        }

        // Secțiunile revin la starea inițială, ca derivarea să nu le găsească validate.
        foreach (OnboardingSectionApproval section in registration.OnboardingSections)
        {
            section.Status = OnboardingSectionStatus.InProgress;
            section.SubmittedAtUtc = null;
            section.ValidatedAtUtc = null;
            section.Note = null;
        }

        registration.OnboardingCompletedAtUtc = null;
    }
}

internal static class DevToolsQueries
{
    /// <summary>Dosarul cu tot ce alimentează derivarea pașilor — aceleași `Include` ca la citire.</summary>
    public static Task<PfaRegistration?> LoadAsync(
        IApplicationDbContext context,
        Guid registrationId,
        CancellationToken cancellationToken) =>
        context.PfaRegistrations
            .Include(r => r.OnboardingSections)
            .Include(r => r.FiscalProfile)
            .Include(r => r.BankAccountDeclaration)
            .Include(r => r.OblioAccount)
            .Include(r => r.SignaturePacket)
            .Include(r => r.CompanyFormationRequest)
            .Include(r => r.ArrAuthorizationRequest)
            .Include(r => r.PlatformAccounts)
            .Include(r => r.Vehicles).ThenInclude(v => v.CopyRequest)
            .FirstOrDefaultAsync(r => r.Id == registrationId, cancellationToken);

    public static void Audit(
        IApplicationDbContext context,
        PfaRegistration registration,
        OnboardingStepKey step,
        string toStatus,
        Guid performedByUserId,
        DateTime nowUtc) =>
        context.OnboardingStepAudits.Add(new OnboardingStepAudit
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            StepKey = OnboardingDevFixtures.KeyOf(step),
            FromStatus = "DevTools",
            ToStatus = toStatus,
            PerformedByUserId = performedByUserId,
            Note = "Unelte de dezvoltare — sesiune de test.",
            CreatedAtUtc = nowUtc,
        });
}

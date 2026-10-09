using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.ArrFleet;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.ArrFleet;

/// <summary>Starea pasului, cum o vede clientul.</summary>
public sealed record GetArrFleetStateQuery(Guid UserId) : IQuery<ArrFleetStateResponse>;

/// <summary>Contul de șofer completat pe o platformă.</summary>
public sealed record ArrFleetDriverAccountInput(
    string Platform,
    bool HasAccount,
    string? Email,
    string? Phone,
    string? FullName);

/// <summary>
/// Salvarea progresului. Un câmp null rămâne neschimbat, ca fiecare ecran să-și trimită doar partea
/// lui. Suma se recalculează pe server din platforme.
/// </summary>
public sealed record SaveArrFleetDraftCommand(
    Guid UserId,
    IReadOnlyList<string>? Platforms,
    IReadOnlyList<ArrFleetDriverAccountInput>? DriverAccounts,
    string? VehicleOwnership) : ICommand<ArrFleetStateResponse>;

/// <summary>Trimiterea pasului. Validarea actelor obligatorii se face aici, pe server.</summary>
public sealed record SubmitArrFleetCommand(Guid UserId) : ICommand<ArrFleetStateResponse>;

internal sealed class GetArrFleetStateQueryHandler(ArrFleetService service)
    : IQueryHandler<GetArrFleetStateQuery, ArrFleetStateResponse>
{
    public async Task<Result<ArrFleetStateResponse>> Handle(GetArrFleetStateQuery query, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await service.LoadForUserAsync(query.UserId, cancellationToken);

        return registration is null
            ? Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.NoRegistration)
            : await service.ToResponseAsync(registration, cancellationToken);
    }
}

internal sealed class SaveArrFleetDraftCommandHandler(
    IApplicationDbContext context,
    ArrFleetService service,
    OnboardingStateService stateService)
    : ICommandHandler<SaveArrFleetDraftCommand, ArrFleetStateResponse>
{
    public async Task<Result<ArrFleetStateResponse>> Handle(SaveArrFleetDraftCommand command, CancellationToken cancellationToken)
    {
        Result guard = await stateService.EnsureWritableAsync(command.UserId, OnboardingStepKey.ArrFleet, cancellationToken);
        if (guard.IsFailure)
        {
            return Result.Failure<ArrFleetStateResponse>(guard.Error);
        }

        PfaRegistration? registration = await service.LoadForUserAsync(command.UserId, cancellationToken);
        if (registration is null)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.NoRegistration);
        }

        DateTime nowUtc = DateTime.UtcNow;
        ArrFleetApplication application = service.Ensure(registration, nowUtc);

        // După trimitere pasul e doar de citit: îl redeschide un agent, cu motiv.
        if (application.SubmittedAtUtc is not null)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.AlreadySubmitted);
        }

        if (command.Platforms is not null)
        {
            ApplyPlatforms(registration, application, command.Platforms, nowUtc);
        }

        if (command.DriverAccounts is not null)
        {
            foreach (ArrFleetDriverAccountInput input in command.DriverAccounts)
            {
                if (Enum.TryParse(input.Platform, ignoreCase: true, out PfaPlatformProvider provider))
                {
                    ApplyDriverAccount(service.AccountFor(registration, provider), input, nowUtc);
                }
            }
        }

        if (command.VehicleOwnership is not null
            && Enum.TryParse(command.VehicleOwnership, ignoreCase: true, out ArrFleetVehicleOwnership ownership))
        {
            await ApplyOwnershipAsync(application, ownership, cancellationToken);
        }

        application.UpdatedAtUtc = nowUtc;
        await context.SaveChangesAsync(cancellationToken);

        return await service.ToResponseAsync(registration, cancellationToken);
    }

    private void ApplyPlatforms(
        PfaRegistration registration,
        ArrFleetApplication application,
        IReadOnlyList<string> platforms,
        DateTime nowUtc)
    {
        ArrFleetPlatforms selected = ArrFleetPlatforms.None;
        foreach (string platform in platforms)
        {
            if (Enum.TryParse(platform, ignoreCase: true, out PfaPlatformProvider provider))
            {
                selected |= ArrFleetRules.FlagOf(provider);
            }
        }

        foreach (PfaPlatformProvider provider in new[] { PfaPlatformProvider.Uber, PfaPlatformProvider.Bolt })
        {
            bool isSelected = selected.HasFlag(ArrFleetRules.FlagOf(provider));
            PfaPlatformAccount? existing = registration.PlatformAccounts.FirstOrDefault(a => a.Provider == provider);
            if (!isSelected && existing is null)
            {
                continue;
            }

            PfaPlatformAccount account = existing ?? service.AccountFor(registration, provider);
            account.IsSelectedByUser = isSelected;
            if (!isSelected)
            {
                account.OnboardingStatus = PfaPlatformOnboardingStatus.Skipped;
            }
            else if (account.OnboardingStatus is PfaPlatformOnboardingStatus.NotStarted or PfaPlatformOnboardingStatus.Skipped)
            {
                account.OnboardingStatus = PfaPlatformOnboardingStatus.Selected;
            }
            account.UpdatedAtUtc = nowUtc;
        }

        long amount = ArrFleetPricing.AmountBani(selected);

        // O dovadă de plată încărcată înainte e pentru suma veche: clientul vede un avertisment.
        if (application.PaymentAmountBani != 0 && amount != application.PaymentAmountBani)
        {
            application.PaymentAmountChangedAtUtc = nowUtc;
        }

        application.Platforms = selected;
        application.PaymentAmountBani = amount;
    }

    private static void ApplyDriverAccount(PfaPlatformAccount account, ArrFleetDriverAccountInput input, DateTime nowUtc)
    {
        account.DriverHasExistingAccount = input.HasAccount;

        // „Nu am cont”: câmpurile nu se cer, iar agentul îl sună pe client.
        if (input.HasAccount)
        {
            account.DriverEmail = string.IsNullOrWhiteSpace(input.Email) ? account.DriverEmail : input.Email.Trim();
            account.DriverPhone = PlatformContactRules.ToE164(input.Phone) ?? account.DriverPhone;
            account.DriverFullName = string.IsNullOrWhiteSpace(input.FullName) ? account.DriverFullName : input.FullName.Trim();
        }

        account.UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Alt mod de deținere: contractul cerut de cel vechi se marchează înlocuit, nu se șterge.
    /// </summary>
    private async Task ApplyOwnershipAsync(
        ArrFleetApplication application,
        ArrFleetVehicleOwnership ownership,
        CancellationToken cancellationToken)
    {
        if (application.VehicleOwnership == ownership)
        {
            return;
        }

        if (ArrFleetRules.OwnershipDocument(application.VehicleOwnership) is ArrFleetRules.Requirement previous)
        {
            List<Document> contracts = await context.Documents
                .Where(d => d.UserId == application.UserId && d.Category == previous.Category && !d.IsSuperseded)
                .ToListAsync(cancellationToken);

            foreach (Document contract in contracts)
            {
                contract.IsSuperseded = true;
            }
        }

        application.VehicleOwnership = ownership;
    }
}

internal sealed class SubmitArrFleetCommandHandler(
    IApplicationDbContext context,
    ArrFleetService service,
    OnboardingStateService stateService)
    : ICommandHandler<SubmitArrFleetCommand, ArrFleetStateResponse>
{
    public async Task<Result<ArrFleetStateResponse>> Handle(SubmitArrFleetCommand command, CancellationToken cancellationToken)
    {
        Result guard = await stateService.EnsureWritableAsync(command.UserId, OnboardingStepKey.ArrFleet, cancellationToken);
        if (guard.IsFailure)
        {
            return Result.Failure<ArrFleetStateResponse>(guard.Error);
        }

        PfaRegistration? registration = await service.LoadForUserAsync(command.UserId, cancellationToken);
        if (registration?.ArrFleetApplication is not ArrFleetApplication application)
        {
            return Result.Failure<ArrFleetStateResponse>(registration is null ? ArrFleetErrors.NoRegistration : ArrFleetErrors.NotFound);
        }

        if (application.SubmittedAtUtc is not null)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.AlreadySubmitted);
        }

        List<Document> documents = await service.DocumentsAsync(command.UserId, cancellationToken);
        IReadOnlyList<string> missing = ArrFleetRules.Missing(
            application,
            registration.PlatformAccounts.Where(a => a.IsSelectedByUser).ToList(),
            documents);

        if (missing.Count > 0)
        {
            return Result.Failure<ArrFleetStateResponse>(ArrFleetErrors.Incomplete(missing));
        }

        DateTime nowUtc = DateTime.UtcNow;
        application.SubmittedAtUtc = nowUtc;
        application.ReopenedReason = null;
        service.SetStatus(application, ArrFleetStatus.DocumentsSubmitted, command.UserId, nowUtc);

        List<Guid> adminIds = await context.Users
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (Guid adminId in adminIds)
        {
            context.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                Text = "Un dosar a trimis pasul „ARR & Cont Flotă”: actele și dovada plății așteaptă verificarea.",
                Type = NotificationTypes.OnboardingStepAwaitingAdmin,
                RelatedUserId = command.UserId,
                IsRead = false,
                CreatedAtUtc = nowUtc,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        return await service.ToResponseAsync(registration, cancellationToken);
    }
}

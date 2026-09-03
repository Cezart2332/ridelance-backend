using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Platforms;

/// <summary>Pasul 4 — userul selectează platformele pe care vrea să lucreze (Uber/Bolt).</summary>
public sealed record SelectPlatformsCommand(Guid UserId, bool UberSelected, bool BoltSelected)
    : ICommand<PlatformOnboardingResponse>;

internal sealed class SelectPlatformsCommandHandler(
    IApplicationDbContext context,
    OnboardingStateService stateService)
    : ICommandHandler<SelectPlatformsCommand, PlatformOnboardingResponse>
{
    public async Task<Result<PlatformOnboardingResponse>> Handle(
        SelectPlatformsCommand command,
        CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        // `allowJustCompleted`: pasul se închide singur în clipa în care credențialele sunt
        // complete, iar salvarea automată din timpul tastării nu are voie să se blocheze pe asta.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Platforms, cancellationToken, allowJustCompleted: true);

        if (guard.IsFailure)
        {
            return Result.Failure<PlatformOnboardingResponse>(guard.Error);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.PlatformAccounts)
            .Include(r => r.FleetConsent)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<PlatformOnboardingResponse>(PlatformShared.NoRegistration);
        }

        Apply(registration, PfaPlatformProvider.Uber, command.UberSelected);
        Apply(registration, PfaPlatformProvider.Bolt, command.BoltSelected);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(PlatformShared.ToResponse(registration));
    }

    private void Apply(PfaRegistration registration, PfaPlatformProvider provider, bool selected)
    {
        PfaPlatformAccount? account = PlatformShared.DriverAccount(registration, provider);

        if (account is null)
        {
            account = new PfaPlatformAccount
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                Provider = provider,
                Kind = PfaPlatformAccountKind.Driver,
            };
            context.PfaPlatformAccounts.Add(account);
            registration.PlatformAccounts.Add(account);
        }

        account.IsSelectedByUser = selected;

        // Frunza devine „nu se aplică" (Skipped) când platforma nu e selectată.
        if (!selected)
        {
            account.OnboardingStatus = PfaPlatformOnboardingStatus.Skipped;
        }
        else if (account.OnboardingStatus is PfaPlatformOnboardingStatus.NotStarted or PfaPlatformOnboardingStatus.Skipped)
        {
            account.OnboardingStatus = PfaPlatformOnboardingStatus.Selected;
        }

        account.UpdatedAtUtc = DateTime.UtcNow;
    }
}

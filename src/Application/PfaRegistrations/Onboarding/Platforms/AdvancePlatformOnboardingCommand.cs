using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Platforms;

/// <summary>Adminul avansează manual statusul de onboarding al unei platforme (fără API extern).</summary>
public sealed record AdvancePlatformOnboardingCommand(
    Guid RegistrationId,
    PfaPlatformProvider Provider,
    PfaPlatformOnboardingStatus OnboardingStatus) : ICommand;

internal sealed class AdvancePlatformOnboardingCommandHandler(IApplicationDbContext context)
    : ICommandHandler<AdvancePlatformOnboardingCommand>
{
    public async Task<Result> Handle(AdvancePlatformOnboardingCommand command, CancellationToken cancellationToken)
    {
        PfaPlatformAccount? account = await context.PfaPlatformAccounts
            .SingleOrDefaultAsync(
                a => a.PfaRegistrationId == command.RegistrationId &&
                     a.Provider == command.Provider &&
                     a.Kind == PfaPlatformAccountKind.Driver,
                cancellationToken);

        if (account is null)
        {
            return Result.Failure(PlatformShared.AccountNotFound);
        }

        account.OnboardingStatus = command.OnboardingStatus;
        account.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

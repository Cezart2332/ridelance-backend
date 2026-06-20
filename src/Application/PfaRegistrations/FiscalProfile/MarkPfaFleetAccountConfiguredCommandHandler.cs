using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.FiscalProfile;

internal sealed class MarkPfaFleetAccountConfiguredCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<MarkPfaFleetAccountConfiguredCommand, PfaPlatformAccountResponse>
{
    public async Task<Result<PfaPlatformAccountResponse>> Handle(
        MarkPfaFleetAccountConfiguredCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> access = await PfaAccess.EnsureCanManageAsync(
            context,
            userContext,
            command.PfaRegistrationId,
            cancellationToken);

        if (access.IsFailure)
        {
            return Result.Failure<PfaPlatformAccountResponse>(access.Error);
        }

        if (!Enum.TryParse(command.Provider, true, out PfaPlatformProvider provider))
        {
            return Result.Failure<PfaPlatformAccountResponse>(
                Error.Failure("PfaPlatformAccount.InvalidProvider", "Invalid fleet account provider."));
        }

        PfaPlatformAccount? account = await context.PfaPlatformAccounts
            .SingleOrDefaultAsync(a =>
                    a.PfaRegistrationId == command.PfaRegistrationId &&
                    a.Provider == provider &&
                    a.Kind == PfaPlatformAccountKind.Fleet,
                cancellationToken);

        if (account is null)
        {
            account = new PfaPlatformAccount
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaRegistrationId,
                Provider = provider,
                Kind = PfaPlatformAccountKind.Fleet
            };
            context.PfaPlatformAccounts.Add(account);
        }

        account.Status = PfaFleetAccountStatus.Configured;
        account.ConfiguredAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        account.UpdatedByUserId = userContext.UserId;

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = access.Value.UserId,
            Type = NotificationTypes.FleetAccountConfigured,
            Text = $"Contul {provider} Fleet a fost configurat de RIDElance.",
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync(cancellationToken);

        return PfaFiscalProfileMapper.MapAccount(account);
    }
}

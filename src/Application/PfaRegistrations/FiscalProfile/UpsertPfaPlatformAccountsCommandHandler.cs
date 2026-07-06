using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.FiscalProfile;

internal sealed class UpsertPfaPlatformAccountsCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ISecretProtector secretProtector)
    : ICommandHandler<UpsertPfaPlatformAccountsCommand, IReadOnlyList<PfaPlatformAccountResponse>>
{
    public async Task<Result<IReadOnlyList<PfaPlatformAccountResponse>>> Handle(
        UpsertPfaPlatformAccountsCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> access = await PfaAccess.EnsureCanViewAsync(
            context,
            userContext,
            command.PfaRegistrationId,
            cancellationToken);

        if (access.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PfaPlatformAccountResponse>>(access.Error);
        }

        // The PFA owner may manage their own Driver accounts, but Fleet accounts
        // (created and operated by RIDElance) remain staff-only.
        bool isOwnerOnly = access.Value.UserId == userContext.UserId;
        if (isOwnerOnly &&
            command.Accounts.Any(i => string.Equals(i.Kind, "Fleet", StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure<IReadOnlyList<PfaPlatformAccountResponse>>(
                Error.Failure("PfaPlatformAccount.Forbidden", "Fleet accounts can only be updated by RIDElance staff."));
        }

        List<PfaPlatformAccount> existingAccounts = await context.PfaPlatformAccounts
            .Where(a => a.PfaRegistrationId == command.PfaRegistrationId)
            .ToListAsync(cancellationToken);

        foreach (UpsertPfaPlatformAccountItem item in command.Accounts)
        {
            if (!Enum.TryParse(item.Provider, true, out PfaPlatformProvider provider) ||
                !Enum.TryParse(item.Kind, true, out PfaPlatformAccountKind kind))
            {
                return Result.Failure<IReadOnlyList<PfaPlatformAccountResponse>>(
                    Error.Failure("PfaPlatformAccount.InvalidValue", "Invalid platform account provider or kind."));
            }

            PfaFleetAccountStatus status = PfaFleetAccountStatus.NotConfigured;
            if (!string.IsNullOrWhiteSpace(item.Status) &&
                !Enum.TryParse(item.Status, true, out status))
            {
                return Result.Failure<IReadOnlyList<PfaPlatformAccountResponse>>(
                    Error.Failure("PfaPlatformAccount.InvalidStatus", "Invalid platform account status."));
            }

            PfaPlatformAccount? account = existingAccounts
                .SingleOrDefault(a => a.Provider == provider && a.Kind == kind);

            if (account is null)
            {
                account = new PfaPlatformAccount
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = command.PfaRegistrationId,
                    Provider = provider,
                    Kind = kind
                };
                context.PfaPlatformAccounts.Add(account);
                existingAccounts.Add(account);
            }

            account.Email = Normalize(item.Email);
            account.Phone = Normalize(item.Phone);
            account.FullName = Normalize(item.FullName);
            account.Status = kind == PfaPlatformAccountKind.Fleet ? status : PfaFleetAccountStatus.Configured;
            account.UpdatedAtUtc = DateTime.UtcNow;
            account.UpdatedByUserId = userContext.UserId;

            if (kind == PfaPlatformAccountKind.Fleet && !string.IsNullOrWhiteSpace(item.Password))
            {
                account.PasswordProtected = secretProtector.Protect(item.Password.Trim());
                account.PasswordUpdatedAtUtc = DateTime.UtcNow;
            }

            if (account.Status == PfaFleetAccountStatus.Configured && account.ConfiguredAtUtc is null)
            {
                account.ConfiguredAtUtc = DateTime.UtcNow;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        IReadOnlyList<PfaPlatformAccountResponse> response = existingAccounts
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.Provider)
            .Select(PfaFiscalProfileMapper.MapAccount)
            .ToList();

        return Result.Success(response);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

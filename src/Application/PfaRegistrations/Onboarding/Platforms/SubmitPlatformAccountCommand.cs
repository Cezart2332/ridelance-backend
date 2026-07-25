using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Platforms;

/// <summary>Pasul 4 — userul completează detaliile contului de operator pe o platformă (fără parolă).</summary>
public sealed record SubmitPlatformAccountCommand(
    Guid UserId,
    PfaPlatformProvider Provider,
    bool HasExistingAccount,
    string? OperatorAccountId,
    Guid? AffiliationContractDocumentId,
    string? ExistingAccountAnswer = null) : ICommand<PlatformOnboardingResponse>;

internal sealed class SubmitPlatformAccountCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SubmitPlatformAccountCommand, PlatformOnboardingResponse>
{
    public async Task<Result<PlatformOnboardingResponse>> Handle(
        SubmitPlatformAccountCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.PlatformAccounts)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<PlatformOnboardingResponse>(PlatformShared.NoRegistration);
        }

        PfaPlatformAccount? account = PlatformShared.DriverAccount(registration, command.Provider);

        if (account is null)
        {
            account = new PfaPlatformAccount
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                Provider = command.Provider,
                Kind = PfaPlatformAccountKind.Driver,
                IsSelectedByUser = true,
            };
            context.PfaPlatformAccounts.Add(account);
            registration.PlatformAccounts.Add(account);
        }

        account.IsSelectedByUser = true;
        account.HasExistingAccount = command.HasExistingAccount;
        account.ExistingAccountAnswer = string.IsNullOrWhiteSpace(command.ExistingAccountAnswer)
            ? null
            : command.ExistingAccountAnswer.Trim();
        account.OperatorAccountId = string.IsNullOrWhiteSpace(command.OperatorAccountId) ? null : command.OperatorAccountId.Trim();
        account.AffiliationContractDocumentId = command.AffiliationContractDocumentId;
        account.UpdatedAtUtc = DateTime.UtcNow;

        // Avans automat până la nivelul suportat de datele completate; restul rămâne manual.
        if (account.AffiliationContractDocumentId is not null)
        {
            account.OnboardingStatus = PfaPlatformOnboardingStatus.ContractSigned;
        }
        else if (!string.IsNullOrWhiteSpace(account.OperatorAccountId) || account.HasExistingAccount)
        {
            account.OnboardingStatus = PfaPlatformOnboardingStatus.AccountLinked;
        }
        else if (account.OnboardingStatus is PfaPlatformOnboardingStatus.NotStarted or PfaPlatformOnboardingStatus.Skipped)
        {
            account.OnboardingStatus = PfaPlatformOnboardingStatus.Selected;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(PlatformShared.ToResponse(registration));
    }
}

using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Platforms;

/// <summary>
/// Pasul 4 — datele contului de flotă al șoferului pe o platformă.
///
/// <paramref name="Password"/> e parola contului de flotă: cea existentă, dacă are deja cont, sau
/// cea dorită, dacă i-l deschidem noi. Se stochează criptată și nu se întoarce niciodată clientului.
/// Null înseamnă „las-o pe cea salvată" — un formular retrimis fără parolă nu o șterge.
/// </summary>
public sealed record SubmitPlatformAccountCommand(
    Guid UserId,
    PfaPlatformProvider Provider,
    bool HasExistingAccount,
    string? OperatorAccountId,
    Guid? AffiliationContractDocumentId,
    string? ExistingAccountAnswer = null,
    string? Email = null,
    string? Phone = null,
    string? Password = null) : ICommand<PlatformOnboardingResponse>;

internal sealed class SubmitPlatformAccountCommandHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector,
    OnboardingStateService stateService)
    : ICommandHandler<SubmitPlatformAccountCommand, PlatformOnboardingResponse>
{
    public async Task<Result<PlatformOnboardingResponse>> Handle(
        SubmitPlatformAccountCommand command,
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

        account.Email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
        account.Phone = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim();

        // Parola nu se șterge la o retrimitere fără ea: formularul nu o primește înapoi de la
        // server, deci ar veni goală la fiecare salvare ulterioară.
        if (!string.IsNullOrWhiteSpace(command.Password))
        {
            account.PasswordProtected = secretProtector.Protect(command.Password);
            account.PasswordUpdatedAtUtc = DateTime.UtcNow;
        }

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

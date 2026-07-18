using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;

namespace Application.Banking;

/// <summary>
/// Verifică la provider starea requisition-ului unei conexiuni și, dacă userul a
/// terminat autorizarea la bancă, salvează conturile și rulează primul sync.
/// Folosit de FinalizeBankConnectionCommand și, oportunist, de GetBankConnectionQuery.
/// </summary>
public sealed class BankConnectionFinalizer(
    IApplicationDbContext context,
    IBankDataProvider provider,
    ISecretProtector secretProtector,
    BankAccountSyncService syncService)
{
    public async Task<BankConnection> FinalizeAsync(
        BankConnection connection,
        CancellationToken cancellationToken)
    {
        string requisitionId = secretProtector.Unprotect(connection.ProviderRequisitionId);

        BankRequisitionDetails details = await provider.GetRequisitionAsync(requisitionId, cancellationToken);

        switch (details.Status)
        {
            case BankRequisitionStatus.Linked:
                await LinkAsync(connection, details, cancellationToken);
                break;

            case BankRequisitionStatus.Expired:
                connection.Status = BankConnectionStatus.Expired;
                connection.ErrorMessage = null;
                break;

            case BankRequisitionStatus.Rejected:
            case BankRequisitionStatus.Suspended:
                connection.Status = BankConnectionStatus.Error;
                connection.ErrorMessage = "Autorizarea la bancă a fost respinsă. Încearcă din nou.";
                break;

            case BankRequisitionStatus.GivingConsent:
            case BankRequisitionStatus.UndergoingAuthentication:
                connection.Status = BankConnectionStatus.Pending;
                break;

            case BankRequisitionStatus.Created:
            default:
                break;
        }

        await context.SaveChangesAsync(cancellationToken);
        return connection;
    }

    private async Task LinkAsync(
        BankConnection connection,
        BankRequisitionDetails details,
        CancellationToken cancellationToken)
    {
        connection.Status = BankConnectionStatus.Linked;
        connection.LinkedAtUtc ??= DateTime.UtcNow;
        connection.ConsentExpiresAtUtc = details.ConsentExpiresAtUtc ?? DateTime.UtcNow.AddDays(90);
        connection.ErrorMessage = null;
        connection.ConsecutiveFailures = 0;
        connection.ExpiryNotifiedAtUtc = null;

        // Upsert pe provider_account_id (unique) — la reconectare, conturile existente
        // sunt mutate pe conexiunea curentă și își păstrează istoricul de tranzacții.
        List<Domain.Banking.BankAccount> userAccounts = await context.BankAccounts
            .Where(ba => ba.UserId == connection.UserId)
            .ToListAsync(cancellationToken);

        foreach (string providerAccountId in details.AccountIds)
        {
            Domain.Banking.BankAccount? account = userAccounts
                .FirstOrDefault(ba => ba.ProviderAccountId == providerAccountId);

            if (account is null)
            {
                account = new Domain.Banking.BankAccount
                {
                    Id = Guid.NewGuid(),
                    BankConnectionId = connection.Id,
                    UserId = connection.UserId,
                    ProviderAccountId = providerAccountId,
                };
                context.BankAccounts.Add(account);
                userAccounts.Add(account);

                try
                {
                    BankAccountDetailsInfo info = await provider.GetAccountDetailsAsync(providerAccountId, cancellationToken);
                    account.IbanMasked = MaskIban(info.Iban);
                    account.Currency = info.Currency;
                    account.OwnerName = info.OwnerName;
                }
                catch (BankDataProviderException)
                {
                    // Detaliile sunt opționale — contul rămâne funcțional și fără ele.
                }
            }

            account.BankConnectionId = connection.Id;
            account.IsActive = true;
        }

        // Conturile care nu mai fac parte din requisition rămân în DB, dar inactive.
        foreach (Domain.Banking.BankAccount account in userAccounts
            .Where(ba => !details.AccountIds.Contains(ba.ProviderAccountId)))
        {
            account.IsActive = false;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Primul sync inline, ca userul să vadă tranzacții imediat după conectare.
        foreach (Domain.Banking.BankAccount account in userAccounts.Where(ba => ba.IsActive))
        {
            try
            {
                await syncService.SyncAccountAsync(account, connection, cancellationToken);
            }
            catch (BankDataRateLimitException ex)
            {
                account.RateLimitedUntilUtc = DateTime.UtcNow.Add(ex.RetryAfter ?? TimeSpan.FromHours(12));
            }
            catch (BankDataProviderException ex)
            {
                connection.ErrorMessage = ex.Message;
            }
        }
    }

    /// <summary>Doar forma mascată a IBAN-ului este stocată (ex. "RO49••••1234").</summary>
    internal static string? MaskIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return null;
        }

        string trimmed = iban.Replace(" ", string.Empty, StringComparison.Ordinal);
        return trimmed.Length <= 8
            ? new string('•', trimmed.Length)
            : $"{trimmed[..4]}••••{trimmed[^4..]}";
    }
}

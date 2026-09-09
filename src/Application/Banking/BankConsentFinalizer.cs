using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Banking;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;

namespace Application.Banking;

/// <summary>
/// Duce o conexiune bancară de la „am trimis omul la bancă" la „avem conturile".
///
/// Furnizorul nu ne sună înapoi: utilizatorul se întoarce în pagina noastră, iar noi întrebăm
/// banca dacă acordul a fost dat. Verificarea se face la citirea stării conexiunii — ecranul face
/// oricum polling cât timp așteaptă — deci nu există niciun endpoint de callback de securizat și
/// nicio fereastră în care o conexiune să rămână „în curs" pentru totdeauna.
///
/// Ce înlocuiește: la furnizorul anterior, linkul nu putea purta o referință de-a noastră și toate
/// conexiunile clienților stăteau la un loc, așa că proprietatea se deducea din ce apăruse nou de
/// la ultimul snapshot. Aici consimțământul e al nostru din prima secundă — îl deschidem noi,
/// înainte ca utilizatorul să atingă banca.
/// </summary>
public sealed class BankConsentFinalizer(
    IApplicationDbContext context,
    IBankDataProvider provider,
    ISecretProtector secretProtector,
    BankAccountSyncService syncService)
{
    /// <summary>Stările în care banca spune că acordul e dat și conturile se pot citi.</summary>
    private static readonly string[] LiveConsentStatuses = ["valid", "partiallyAuthorised"];

    public async Task<BankConnectionStatus> TryFinalizeAsync(
        BankConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.Status is BankConnectionStatus.Linked or BankConnectionStatus.Revoked)
        {
            return connection.Status;
        }

        // Rând rămas de la un furnizor care nu mai există. Nu e o conectare în curs și nu are cum
        // să devină una — altfel ecranul ar aștepta la nesfârșit o confirmare imposibilă.
        if (!string.Equals(connection.Provider, provider.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            connection.Status = BankConnectionStatus.Revoked;
            connection.ErrorMessage = null;
            await context.SaveChangesAsync(cancellationToken);
            return connection.Status;
        }

        if (string.IsNullOrWhiteSpace(connection.ProviderConsentId))
        {
            connection.Status = BankConnectionStatus.Revoked;
            await context.SaveChangesAsync(cancellationToken);
            return connection.Status;
        }

        BankConsentState state;

        try
        {
            state = await provider.GetConsentAsync(
                connection.InstitutionId,
                connection.ProviderConsentId,
                cancellationToken);
        }
        catch (BankDataConsentExpiredException ex)
        {
            connection.Status = BankConnectionStatus.Expired;
            connection.ErrorMessage = ex.Message;
            await context.SaveChangesAsync(cancellationToken);
            return connection.Status;
        }

        connection.ConsentStatus = state.Status;

        if (state.Status is null || !LiveConsentStatuses.Contains(state.Status, StringComparer.OrdinalIgnoreCase))
        {
            // Încă nu a terminat autorizarea. Se aruncă doar dacă a trecut și termenul adresei:
            // altfel un utilizator care lasă tabul băncii deschis o oră ar pierde conexiunea.
            if (connection.LinkExpiresAtUtc is { } expiry && expiry < DateTime.UtcNow)
            {
                connection.Status = BankConnectionStatus.Error;
                connection.ErrorMessage = "Autorizarea la bancă nu a fost finalizată la timp. Reia conectarea.";
            }

            await context.SaveChangesAsync(cancellationToken);
            return connection.Status;
        }

        connection.Status = BankConnectionStatus.Linked;
        connection.LinkedAtUtc = DateTime.UtcNow;
        connection.ConsentExpiresAtUtc = state.ValidUntilUtc;
        connection.ErrorMessage = null;
        connection.ConsecutiveFailures = 0;

        await context.SaveChangesAsync(cancellationToken);

        await LinkAccountsAsync(connection, cancellationToken);

        return connection.Status;
    }

    /// <summary>Citește conturile acoperite de acord și le aduce la zi la noi.</summary>
    private async Task LinkAccountsAsync(BankConnection connection, CancellationToken cancellationToken)
    {
        IReadOnlyList<BankAccountDetailsInfo> remote = await provider.ListAccountsAsync(
            connection.InstitutionId,
            connection.ProviderConsentId,
            cancellationToken);

        List<BankAccount> existing = await context.BankAccounts
            .Where(a => a.BankConnectionId == connection.Id)
            .ToListAsync(cancellationToken);

        foreach (BankAccountDetailsInfo info in remote)
        {
            BankAccount? account = existing.Find(a => a.ProviderAccountId == info.ResourceId);

            if (account is null)
            {
                account = new BankAccount
                {
                    Id = Guid.NewGuid(),
                    BankConnectionId = connection.Id,
                    UserId = connection.UserId,
                    ProviderAccountId = info.ResourceId,
                };
                context.BankAccounts.Add(account);
                existing.Add(account);
            }

            account.IbanMasked = MaskIban(info.Iban) ?? account.IbanMasked;
            account.Currency = info.Currency ?? account.Currency;
            account.OwnerName = info.OwnerName ?? account.OwnerName;
            account.IsActive = true;
        }

        // Conturile care nu mai apar în acord ies din circuit, dar istoricul lor rămâne.
        foreach (BankAccount stale in existing.Where(a => !remote.Any(r => r.ResourceId == a.ProviderAccountId)))
        {
            stale.IsActive = false;
        }

        await context.SaveChangesAsync(cancellationToken);

        await FillPfaDeclarationAsync(connection, remote, cancellationToken);

        foreach (BankAccount account in existing.Where(a => a.IsActive))
        {
            await syncService.SyncAccountAsync(account, connection, cancellationToken);
        }
    }

    /// <summary>
    /// Contul conectat completează și declarația bancară din înrolarea PFA.
    ///
    /// Ăsta e motivul pentru care extrasul de cont nu se mai cere: datele pe care le citeam de pe
    /// o poză — IBAN, titular, bancă — vin acum de la bancă, semnate de ea. O declarație venită
    /// pe drumul ăsta e verificată din start; nu mai are ce valida un om.
    /// </summary>
    private async Task FillPfaDeclarationAsync(
        BankConnection connection,
        IReadOnlyList<BankAccountDetailsInfo> accounts,
        CancellationToken cancellationToken)
    {
        // Conturile de card n-au IBAN și nu sunt contul de PFA; primul cont în lei cu IBAN e.
        BankAccountDetailsInfo? primary = accounts.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Iban) &&
            (a.Currency is null || a.Currency.Equals("RON", StringComparison.OrdinalIgnoreCase)));

        if (primary?.Iban is null)
        {
            return;
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.BankAccountDeclaration)
            .Where(r => r.UserId == connection.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        PfaBankAccountDeclaration declaration = registration.BankAccountDeclaration ?? new PfaBankAccountDeclaration
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };

        if (registration.BankAccountDeclaration is null)
        {
            context.PfaBankAccountDeclarations.Add(declaration);
        }

        declaration.BankName = string.IsNullOrWhiteSpace(connection.InstitutionName)
            ? declaration.BankName
            : connection.InstitutionName;
        declaration.IbanEncrypted = secretProtector.Protect(primary.Iban);
        declaration.IbanMasked = MaskIban(primary.Iban);
        declaration.BankConnectionId = connection.Id;
        declaration.Source = BankDeclarationSource.OpenBanking;
        declaration.Status = BankDeclarationStatus.Verified;
        declaration.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>„RO49••••1234" — IBAN-ul complet nu se stochează niciodată în clar.</summary>
    internal static string? MaskIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
        {
            return null;
        }

        string trimmed = iban.Replace(" ", string.Empty, StringComparison.Ordinal);
        return trimmed.Length <= 8 ? trimmed : $"{trimmed[..4]}••••{trimmed[^4..]}";
    }
}

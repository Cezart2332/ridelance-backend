using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;

namespace Application.Banking;

/// <param name="Candidates">Populat doar în cazul ambiguu, ca utilizatorul să poată alege.</param>
public sealed record BankClaimOutcome(
    BankConnectionStatus Status,
    IReadOnlyList<BankProviderConnection> Candidates);

/// <summary>
/// Decide a cui e o conexiune bancară nou apărută la provider.
///
/// Contextul care face serviciul necesar: linkul de conectare nu poate purta nicio referință
/// de-a noastră, iar toate conexiunile clienților stau în același cont de provider. Deci după
/// ce cineva termină conectarea, singurul lucru observabil e că a apărut o conexiune nouă.
///
/// Regula, deliberat conservatoare: se revendică automat <b>numai</b> când există exact un
/// candidat nou și exact un link în așteptare în tot sistemul. Orice altceva — doi candidați,
/// două linkuri deschise simultan — trece în alegere manuală. Nu se ghicește niciodată, pentru
/// că o ghiceală greșită arată extrasul unui client altuia.
/// </summary>
public sealed class BankConnectionClaimService(
    IApplicationDbContext context,
    IBankDataProvider provider,
    ISecretProtector secretProtector,
    BankAccountSyncService syncService)
{
    /// <summary>Snapshotul conexiunilor existente, luat înainte de a minta un link.</summary>
    public static string SerializeKnown(IEnumerable<string> connectionIds) =>
        JsonSerializer.Serialize(connectionIds.ToArray());

    public async Task<BankClaimOutcome> TryClaimAsync(
        BankConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.Status is BankConnectionStatus.Linked or BankConnectionStatus.Revoked)
        {
            return new BankClaimOutcome(connection.Status, []);
        }

        // Rând rămas de la un provider care nu mai există. Nu e o conectare în curs și nu are
        // cum să devină una — altfel ecranul ar aștepta la nesfârșit o confirmare imposibilă.
        if (!string.Equals(connection.Provider, provider.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            connection.Status = BankConnectionStatus.Revoked;
            connection.ErrorMessage = null;
            await context.SaveChangesAsync(cancellationToken);
            return new BankClaimOutcome(connection.Status, []);
        }

        // Un rând fără termen de link e dinaintea acestei versiuni: nu poate fi revendicat.
        if (connection.LinkExpiresAtUtc is null)
        {
            connection.Status = BankConnectionStatus.Revoked;
            await context.SaveChangesAsync(cancellationToken);
            return new BankClaimOutcome(connection.Status, []);
        }

        // Linkul expirat nu mai poate produce o conexiune: ce apare după el aparține altcuiva.
        if (connection.LinkExpiresAtUtc is { } expiry && expiry < DateTime.UtcNow)
        {
            connection.Status = BankConnectionStatus.Error;
            connection.ErrorMessage = "Linkul de conectare a expirat. Reia conectarea băncii.";
            await context.SaveChangesAsync(cancellationToken);
            return new BankClaimOutcome(connection.Status, []);
        }

        IReadOnlyList<BankProviderConnection> candidates = await FindCandidatesAsync(connection, cancellationToken);

        if (candidates.Count == 0)
        {
            return new BankClaimOutcome(connection.Status, []);
        }

        // Câte linkuri sunt deschise în tot sistemul, nu doar al acestui utilizator: două
        // conectări simultane fac diferența ambiguă chiar dacă fiecare vede un singur candidat.
        // Rândurile fără termen sunt retrase mai sus, deci un link deschis are întotdeauna
        // un termen încă valabil.
        int pendingLinks = await context.BankConnections
            .CountAsync(
                c => (c.Status == BankConnectionStatus.Created || c.Status == BankConnectionStatus.Pending) &&
                     c.LinkExpiresAtUtc != null &&
                     c.LinkExpiresAtUtc > DateTime.UtcNow,
                cancellationToken);

        if (candidates.Count > 1 || pendingLinks > 1)
        {
            connection.Status = BankConnectionStatus.Pending;
            await context.SaveChangesAsync(cancellationToken);
            return new BankClaimOutcome(BankConnectionStatus.Pending, candidates);
        }

        await ClaimAsync(connection, candidates[0], BankClaimMode.Auto, candidates.Count, cancellationToken);
        return new BankClaimOutcome(connection.Status, []);
    }

    /// <summary>Alegerea explicită a utilizatorului dintre candidații returnați mai sus.</summary>
    public async Task<BankClaimOutcome> ClaimChosenAsync(
        BankConnection connection,
        string providerConnectionId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BankProviderConnection> candidates = await FindCandidatesAsync(connection, cancellationToken);
        BankProviderConnection? chosen = candidates.SingleOrDefault(c => c.Id == providerConnectionId);

        if (chosen is null)
        {
            // Fie a revendicat-o altcineva între timp, fie id-ul nu e dintre candidați.
            return new BankClaimOutcome(connection.Status, candidates);
        }

        await ClaimAsync(connection, chosen, BankClaimMode.Manual, candidates.Count, cancellationToken);
        return new BankClaimOutcome(connection.Status, []);
    }

    /// <summary>Conexiunile din provider care nu existau la mintare și nu aparțin nimănui.</summary>
    public async Task<IReadOnlyList<BankProviderConnection>> FindCandidatesAsync(
        BankConnection connection,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BankProviderConnection> all = await provider.ListConnectionsAsync(cancellationToken);

        HashSet<string> known = Deserialize(connection.KnownConnectionIdsJson);
        HashSet<string> claimed = await context.BankConnectionClaims
            .AsNoTracking()
            .Select(c => c.ProviderConnectionId)
            .ToHashSetAsync(cancellationToken);

        return [.. all.Where(c => !known.Contains(c.Id) && !claimed.Contains(c.Id))];
    }

    private async Task ClaimAsync(
        BankConnection connection,
        BankProviderConnection chosen,
        BankClaimMode mode,
        int candidateCount,
        CancellationToken cancellationToken)
    {
        connection.ProviderRequisitionId = secretProtector.Protect(chosen.Id);
        connection.Status = BankConnectionStatus.Linked;
        connection.LinkedAtUtc = DateTime.UtcNow;
        connection.ErrorMessage = null;
        connection.ConsecutiveFailures = 0;

        if (!string.IsNullOrWhiteSpace(chosen.InstitutionName))
        {
            connection.InstitutionName = chosen.InstitutionName;
        }

        if (!string.IsNullOrWhiteSpace(chosen.InstitutionId))
        {
            connection.InstitutionId = chosen.InstitutionId;
        }

        connection.InstitutionLogoUrl ??= chosen.InstitutionLogo;

        // Indexul unic pe ProviderConnectionId respinge o a doua revendicare a aceleiași
        // conexiuni — jurnalul e și registru, nu doar urmă.
        context.BankConnectionClaims.Add(new BankConnectionClaim
        {
            Id = Guid.NewGuid(),
            UserId = connection.UserId,
            BankConnectionId = connection.Id,
            ProviderConnectionId = chosen.Id,
            Mode = mode,
            CandidateCount = candidateCount,
            ClaimedAtUtc = DateTime.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        await LinkAccountsAsync(connection, chosen.Id, cancellationToken);
    }

    private async Task LinkAccountsAsync(
        BankConnection connection,
        string providerConnectionId,
        CancellationToken cancellationToken)
    {
        // Best-effort: dacă providerul nu apucă să sincronizeze acum, jobul o face oricum.
        try
        {
            await provider.TriggerSyncAsync(providerConnectionId, cancellationToken);
        }
        catch (BankDataProviderException)
        {
            // Sincronizarea imediată e un confort, nu o condiție a revendicării.
        }

        IReadOnlyList<string> accountIds = await provider.ListAccountsAsync(providerConnectionId, cancellationToken);

        List<BankAccount> existing = await context.BankAccounts
            .Where(a => a.UserId == connection.UserId)
            .ToListAsync(cancellationToken);

        foreach (string accountId in accountIds)
        {
            BankAccount? account = existing.SingleOrDefault(a => a.ProviderAccountId == accountId);

            if (account is null)
            {
                account = new BankAccount
                {
                    Id = Guid.NewGuid(),
                    BankConnectionId = connection.Id,
                    UserId = connection.UserId,
                    ProviderAccountId = accountId,
                };
                context.BankAccounts.Add(account);
                existing.Add(account);

                BankAccountDetailsInfo details = await provider.GetAccountDetailsAsync(accountId, cancellationToken);
                account.IbanMasked = MaskIban(details.Iban);
                account.Currency = details.Currency;
                account.OwnerName = details.OwnerName;
            }
            else
            {
                account.BankConnectionId = connection.Id;
            }

            account.IsActive = true;
        }

        // Conturile care nu mai apar la provider ies din circuit, dar istoricul lor rămâne.
        foreach (BankAccount stale in existing.Where(a => !accountIds.Contains(a.ProviderAccountId)))
        {
            stale.IsActive = false;
        }

        await context.SaveChangesAsync(cancellationToken);

        foreach (BankAccount account in existing.Where(a => a.IsActive))
        {
            await syncService.SyncAccountAsync(account, connection, cancellationToken);
        }
    }

    private static HashSet<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return [.. JsonSerializer.Deserialize<string[]>(json) ?? []];
        }
        catch (JsonException)
        {
            // Un snapshot ilizibil ar face orice conexiune să pară nouă. Mai sigur e să nu
            // considerăm nimic candidat decât să revendicăm ce nu trebuie.
            return [];
        }
    }

    /// <summary>„RO49••••1234" — IBAN-ul complet nu se stochează niciodată.</summary>
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

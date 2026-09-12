using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Banking;

/// <summary>
/// Tokenurile unui consimțământ, ținute pe rândul conexiunii bancare.
///
/// Cele două tokenuri se păstrează criptate, ca orice secret din platformă. Identificatorul
/// consimțământului, în schimb, stă în clar: după el se caută rândul, iar cifrarea AES-GCM
/// folosește un nonce aleator, deci același identificator dă de fiecare dată alt text — nu se
/// poate căuta după el. Nici nu e mare pierdere: fără certificatul nostru de client și fără
/// tokenuri, identificatorul singur nu deschide nimic.
/// </summary>
internal sealed class BankConsentTokenStore(
    IApplicationDbContext context,
    ISecretProtector secretProtector)
    : IBankConsentTokenStore
{
    public async Task<BankConsentTokens?> GetAsync(string consentId, CancellationToken cancellationToken = default)
    {
        var row = await context.BankConnections
            .AsNoTracking()
            .Where(c => c.ProviderConsentId == consentId)
            .Select(c => new
            {
                c.AccessTokenEncrypted,
                c.RefreshTokenEncrypted,
                c.AccessTokenExpiresAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null ||
            string.IsNullOrEmpty(row.AccessTokenEncrypted) ||
            string.IsNullOrEmpty(row.RefreshTokenEncrypted))
        {
            return null;
        }

        return new BankConsentTokens(
            secretProtector.Unprotect(row.AccessTokenEncrypted),
            secretProtector.Unprotect(row.RefreshTokenEncrypted),
            row.AccessTokenExpiresAtUtc ?? DateTime.UtcNow);
    }

    public Task<string?> GetPsuIpAddressAsync(string consentId, CancellationToken cancellationToken = default) =>
        context.BankConnections
            .AsNoTracking()
            .Where(c => c.ProviderConsentId == consentId)
            .Select(c => c.PsuIpAddress)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SaveAsync(
        string consentId,
        BankConsentTokens tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        BankConnection? connection = await context.BankConnections
            .FirstOrDefaultAsync(c => c.ProviderConsentId == consentId, cancellationToken);

        if (connection is null)
        {
            // Consimțământ fără rând la noi: nu-l putem lega de nimeni. Nu e o eroare de tratat aici
            // — apelantul își va primi 401-ul la următorul apel, cu mesajul lui.
            return;
        }

        connection.AccessTokenEncrypted = secretProtector.Protect(tokens.AccessToken);
        connection.RefreshTokenEncrypted = secretProtector.Protect(tokens.RefreshToken);
        connection.AccessTokenExpiresAtUtc = tokens.AccessExpiresAtUtc;

        await context.SaveChangesAsync(cancellationToken);
    }
}

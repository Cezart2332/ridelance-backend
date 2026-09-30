using System.Security.Cryptography;
using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Accounting.Ledger;
using Domain.Banking;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// O singură dată, la pornire: IBAN-urile salvate mascate („RO49••••1234”) devin complete. Declarația
/// contului are IBAN-ul și criptat, deci se recuperează de acolo; contul bancar din aceeași conexiune,
/// cu aceeași mască, primește același IBAN. Ce nu se poate recupera rămâne mascat până la
/// reconectarea băncii. Idempotent: a doua rulare nu mai găsește nimic de completat.
/// </summary>
internal sealed class IbanBackfillJob(IServiceScopeFactory scopeFactory, ILogger<IbanBackfillJob> logger) : BackgroundService
{
    private const string MaskMarker = "•";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IApplicationDbContext db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            ISecretProtector protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            (int declarations, int accounts) = await BackfillAsync(db, protector, stoppingToken);
            if (declarations + accounts > 0)
            {
                logger.LogInformation("IBAN-uri completate: {Declarations} declarații, {Accounts} conturi.", declarations, accounts);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Completarea IBAN-urilor mascate a eșuat.");
        }
    }

    internal static async Task<(int Declarations, int Accounts)> BackfillAsync(IApplicationDbContext db, ISecretProtector protector, CancellationToken cancellationToken)
    {
        List<PfaBankAccountDeclaration> masked = await db.PfaBankAccountDeclarations
            .Where(d => d.IbanEncrypted != null && (d.Iban == null || d.Iban.Contains(MaskMarker)))
            .ToListAsync(cancellationToken);

        int declarations = 0;
        foreach (PfaBankAccountDeclaration declaration in masked)
        {
            try
            {
                declaration.Iban = CounterpartyRules.NormalizeIban(protector.Unprotect(declaration.IbanEncrypted!));
                declarations++;
            }
            catch (CryptographicException)
            {
                // Cheia de criptare s-a schimbat: IBAN-ul rămâne mascat până la reconectare.
            }
        }

        List<PfaBankAccountDeclaration> known = await db.PfaBankAccountDeclarations
            .Where(d => d.BankConnectionId != null && d.Iban != null && !d.Iban.Contains(MaskMarker))
            .ToListAsync(cancellationToken);
        known.AddRange(masked.Where(d => d.BankConnectionId != null && d.Iban is not null && !d.Iban.Contains(MaskMarker, StringComparison.Ordinal)));

        List<BankAccount> accounts = await db.BankAccounts
            .Where(a => a.Iban != null && a.Iban.Contains(MaskMarker))
            .ToListAsync(cancellationToken);
        int filled = 0;
        foreach (BankAccount account in accounts)
        {
            string? iban = known
                .Where(d => d.BankConnectionId == account.BankConnectionId && CounterpartyRules.MaskIban(d.Iban) == account.Iban)
                .Select(d => d.Iban)
                .FirstOrDefault();
            if (iban is not null)
            {
                account.Iban = iban;
                filled++;
            }
        }

        if (declarations + filled > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return (declarations, filled);
    }
}

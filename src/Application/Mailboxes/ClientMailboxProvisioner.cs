using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.Mailboxes;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Application.Mailboxes;

/// <summary>
/// Crearea adresei operaționale a unui client: mailbox-ul, apoi identitatea RIDElance.
///
/// Idempotentă. Fiecare pas își salvează rezultatul înainte de următorul, iar partea locală și
/// parola se rezervă în baza de date înainte de apelul la furnizor. O reluare după un eșec parțial
/// regăsește aceeași adresă și continuă de unde a rămas: nu apare niciodată un al doilea mailbox
/// pentru același client.
/// </summary>
internal sealed class ClientMailboxProvisioner(
    IApplicationDbContext db,
    IMailboxProvider provider,
    IMailboxCredentialProtector secrets,
    IOptions<MailboxOptions> options)
{
    /// <summary>Peste atâtea omonime ceva e greșit; nu căutăm la nesfârșit.</summary>
    private const int MaxCollisionAttempts = 50;

    public static void Audit(IApplicationDbContext db, ClientMailbox mailbox, ClientMailboxAuditAction action, Guid? performedBy, string? details = null)
    {
        var log = new ClientMailboxAuditLog
        {
            Id = Guid.NewGuid(),
            ClientMailboxId = mailbox.Id,
            Action = action,
            PerformedByUserId = performedBy,
            PerformedAtUtc = DateTime.UtcNow,
            Details = details,
        };
        db.ClientMailboxAuditLogs.Add(log);
    }

    /// <summary>Rulează crearea pentru un mailbox aflat în <see cref="ClientMailboxStatus.Creating"/>.</summary>
    public async Task ProvisionAsync(ClientMailbox mailbox, CancellationToken cancellationToken)
    {
        if (mailbox.Status != ClientMailboxStatus.Creating)
        {
            return;
        }

        try
        {
            var holder = await db.PfaRegistrations.AsNoTracking()
                .Where(p => p.Id == mailbox.PfaRegistrationId)
                .Select(p => new { p.User.FirstName, p.User.LastName })
                .SingleAsync(cancellationToken);
            string displayName = $"{holder.FirstName} {holder.LastName}".Trim();

            await EnsureMailboxAsync(mailbox, holder.FirstName, holder.LastName, displayName, cancellationToken);
            await EnsureIdentityAsync(mailbox, displayName, cancellationToken);

            mailbox.Status = ClientMailboxStatus.Active;
            mailbox.ActivatedAtUtc = DateTime.UtcNow;
            mailbox.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is MailboxProviderException or InvalidOperationException)
        {
            await FailAsync(mailbox, exception.Message, cancellationToken);
        }
    }

    private async Task EnsureMailboxAsync(ClientMailbox mailbox, string? firstName, string? lastName, string displayName, CancellationToken cancellationToken)
    {
        if (mailbox.MailboxCreatedAtUtc is not null)
        {
            return;
        }

        string domain = options.Value.Domain;

        if (mailbox.LocalPart is null)
        {
            // Rezervarea: adresa și parola intră în baza de date înainte să existe la furnizor.
            string localPart = await FreeLocalPartAsync(firstName, lastName, cancellationToken);
            mailbox.LocalPart = localPart;
            mailbox.Address = MailboxAddress.Compose(localPart, domain);
            mailbox.MailboxPasswordEncrypted = secrets.Protect(MailboxPasswords.Generate());
            await db.SaveChangesAsync(cancellationToken);
        }

        string password = secrets.Unprotect(mailbox.MailboxPasswordEncrypted!);

        // Adresa era deja rezervată pentru clientul ăsta: dacă există la furnizor, e a lui, creată de
        // o rulare care n-a apucat să noteze. Îi punem parola pe care o avem și mergem mai departe.
        if (await provider.MailboxExistsAsync(mailbox.LocalPart, cancellationToken))
        {
            await provider.SetMailboxPasswordAsync(mailbox.LocalPart, password, cancellationToken);
        }
        else
        {
            await provider.CreateMailboxAsync(new CreateMailboxRequest(displayName, mailbox.LocalPart, password), cancellationToken);
        }

        mailbox.MailboxCreatedAtUtc = DateTime.UtcNow;
        Audit(db, mailbox, ClientMailboxAuditAction.Created, null, mailbox.Address);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureIdentityAsync(ClientMailbox mailbox, string displayName, CancellationToken cancellationToken)
    {
        if (mailbox.OpsIdentityCreatedAtUtc is not null)
        {
            return;
        }

        bool retry = mailbox.OpsIdentityLocalPart is not null;
        if (!retry)
        {
            mailbox.OpsIdentityLocalPart = MailboxAddress.OpsIdentityOf(mailbox.LocalPart!);
            mailbox.OpsIdentityAddress = MailboxAddress.Compose(mailbox.OpsIdentityLocalPart, options.Value.Domain);
            mailbox.OpsIdentityPasswordEncrypted = secrets.Protect(MailboxPasswords.Generate());
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            // O rulare anterioară poate s-o fi creat fără să noteze. O scoatem și o refacem cu parola
            // pe care o avem: altfel am rămâne cu o identitate a cărei parolă n-o știe nimeni.
            await provider.DeleteIdentityAsync(mailbox.LocalPart!, mailbox.OpsIdentityLocalPart!, cancellationToken);
        }

        await provider.CreateIdentityAsync(
            mailbox.LocalPart!,
            new CreateIdentityRequest($"RIDElance — {displayName}", mailbox.OpsIdentityLocalPart!, secrets.Unprotect(mailbox.OpsIdentityPasswordEncrypted!)),
            cancellationToken);

        mailbox.OpsIdentityCreatedAtUtc = DateTime.UtcNow;
        Audit(db, mailbox, ClientMailboxAuditAction.IdentityCreated, null, mailbox.OpsIdentityAddress);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Prima parte locală liberă: și la noi, și la furnizor.</summary>
    private async Task<string> FreeLocalPartAsync(string? firstName, string? lastName, CancellationToken cancellationToken)
    {
        string baseLocalPart = MailboxAddress.BaseLocalPart(firstName, lastName)
            ?? throw new InvalidOperationException("Adresa nu se poate genera: numele titularului lipsește sau nu are litere latine.");

        for (int attempt = 1; attempt <= MaxCollisionAttempts; attempt++)
        {
            string candidate = MailboxAddress.WithSuffix(baseLocalPart, attempt);
            if (await db.ClientMailboxes.AnyAsync(m => m.LocalPart == candidate, cancellationToken))
            {
                continue;
            }

            if (!await provider.MailboxExistsAsync(candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Nu am găsit o adresă liberă pentru „{baseLocalPart}”.");
    }

    private async Task FailAsync(ClientMailbox mailbox, string error, CancellationToken cancellationToken)
    {
        mailbox.Status = ClientMailboxStatus.Failed;
        mailbox.LastError = error.Length > 1000 ? error[..1000] : error;
        Audit(db, mailbox, ClientMailboxAuditAction.Failed, null, mailbox.LastError);

        List<Guid> adminIds = await db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).ToListAsync(cancellationToken);
        foreach (Guid adminId in adminIds)
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                Text = "Emailul operațional al unui client nu s-a putut crea. Deschide dosarul și apasă „Reîncearcă”.",
                Type = NotificationTypes.OnboardingStepAwaitingAdmin,
                RelatedUserId = mailbox.UserId,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Dosarele ajunse la „ARR &amp; Cont Flotă” (pachetul de semnături validat de admin, deci PFA-ul
    /// există) și încă nefinalizate, care n-au adresă: le pune în coadă. Întoarce câte a adăugat.
    /// </summary>
    public async Task<int> EnqueueDueAsync(CancellationToken cancellationToken)
    {
        var due = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.SignaturePacket != null
                        && p.SignaturePacket.Status == SignaturePacketStatus.Completed
                        && p.User.DeletedAtUtc == null
                        // Doar cine e acum la pas. Clienții care l-au încheiat demult nu primesc adresă
                        // din oficiu; pentru ei rămâne butonul din admin.
                        && (p.ArrFleetApplication == null || p.ArrFleetApplication.Status != Domain.PfaRegistrations.ArrFleet.ArrFleetStatus.Completed)
                        && !db.ClientMailboxes.Any(m => m.UserId == p.UserId))
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new { p.Id, p.UserId })
            .ToListAsync(cancellationToken);

        // Un client cu două dosare primește o singură adresă.
        foreach (var registration in due.DistinctBy(p => p.UserId))
        {
            db.ClientMailboxes.Add(new ClientMailbox
            {
                Id = Guid.NewGuid(),
                UserId = registration.UserId,
                PfaRegistrationId = registration.Id,
                Status = ClientMailboxStatus.Creating,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }
}

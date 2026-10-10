using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.Mailboxes;
using Domain.Notifications;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Mailboxes;

/// <summary>Emailul operațional, cum îl vede adminul. Fără parole: ele ies doar prin afișarea auditată.</summary>
public sealed record ClientMailboxDto(
    string Status,
    string? Address,
    string? OpsIdentityAddress,
    string? LastError,
    DateTime? ActivatedAtUtc,
    DateTime? TransferredAtUtc,
    Guid? HandoverDocumentId);

/// <summary>Credențialele identității RIDElance, pentru Thunderbird. Fiecare citire e auditată.</summary>
public sealed record OpsCredentialsDto(string Address, string Password);

/// <summary>Ce vede clientul: doar adresa. Parola o primește la încheierea colaborării.</summary>
public sealed record OwnMailboxDto(string Address);

/// <param name="Alert">Peste 80% din oricare limită: semnal că trebuie făcut upgrade de plan.</param>
public sealed record MailboxUsageDto(
    int IncomingToday,
    int IncomingLimit,
    int OutgoingToday,
    int OutgoingLimit,
    decimal StorageGb,
    bool Alert);

public static class MailboxErrors
{
    public static readonly Error PfaNotFound = Error.NotFound("Mailbox.PfaNotFound", "Dosarul nu există.");

    public static readonly Error NotCreated = Error.NotFound("Mailbox.NotCreated", "Clientul nu are încă email operațional.");

    public static readonly Error NotActive = Error.Conflict("Mailbox.NotActive", "Emailul operațional nu este activ.");

    public static readonly Error AlreadyExists = Error.Conflict("Mailbox.AlreadyExists", "Emailul operațional există deja sau e în curs de creare.");

    public static readonly Error Transferred = Error.Conflict("Mailbox.Transferred", "Emailul a fost predat clientului.");

    public static Error Provider(string message) => Error.Problem("Mailbox.Provider", message);
}

internal static class ClientMailboxDtos
{
    public static readonly ClientMailboxDto None = new(nameof(ClientMailboxStatus.NotCreated), null, null, null, null, null, null);

    public static ClientMailboxDto Of(ClientMailbox mailbox) => new(
        mailbox.Status.ToString(),
        mailbox.Address,
        // Identitatea se arată abia când există la furnizor; după predare a fost ștearsă.
        mailbox.Status == ClientMailboxStatus.Active ? mailbox.OpsIdentityAddress : null,
        mailbox.LastError,
        mailbox.ActivatedAtUtc,
        mailbox.TransferredAtUtc,
        mailbox.HandoverDocumentId);

    public static Task<ClientMailbox?> ForPfaAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken) =>
        db.ClientMailboxes.FirstOrDefaultAsync(
            m => m.PfaRegistrationId == pfaId || db.PfaRegistrations.Any(p => p.Id == pfaId && p.UserId == m.UserId),
            cancellationToken);
}

/// <summary><c>GET /admin/onboarding/{id}/mailbox</c></summary>
public sealed record GetClientMailboxQuery(Guid PfaId) : IQuery<ClientMailboxDto>;

internal sealed class GetClientMailboxQueryHandler(IApplicationDbContext db) : IQueryHandler<GetClientMailboxQuery, ClientMailboxDto>
{
    public async Task<Result<ClientMailboxDto>> Handle(GetClientMailboxQuery query, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken))
        {
            return Result.Failure<ClientMailboxDto>(MailboxErrors.PfaNotFound);
        }

        ClientMailbox? mailbox = await ClientMailboxDtos.ForPfaAsync(db, query.PfaId, cancellationToken);
        return mailbox is null ? ClientMailboxDtos.None : ClientMailboxDtos.Of(mailbox);
    }
}

/// <summary>
/// <c>POST /admin/onboarding/{id}/mailbox</c> — „Creează email operațional” sau „Reîncearcă”: pune
/// crearea în coadă. O face jobul, în fundal; reluarea continuă de unde a rămas.
/// </summary>
public sealed record RequestClientMailboxCommand(Guid PfaId) : ICommand<ClientMailboxDto>;

internal sealed class RequestClientMailboxCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<RequestClientMailboxCommand, ClientMailboxDto>
{
    public async Task<Result<ClientMailboxDto>> Handle(RequestClientMailboxCommand command, CancellationToken cancellationToken)
    {
        var registration = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.Id == command.PfaId)
            .Select(p => new { p.Id, p.UserId })
            .FirstOrDefaultAsync(cancellationToken);
        if (registration is null)
        {
            return Result.Failure<ClientMailboxDto>(MailboxErrors.PfaNotFound);
        }

        ClientMailbox? mailbox = await ClientMailboxDtos.ForPfaAsync(db, command.PfaId, cancellationToken);
        if (mailbox is null)
        {
            mailbox = new ClientMailbox
            {
                Id = Guid.NewGuid(),
                UserId = registration.UserId,
                PfaRegistrationId = registration.Id,
                Status = ClientMailboxStatus.Creating,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.ClientMailboxes.Add(mailbox);
        }
        else if (mailbox.Status is ClientMailboxStatus.Failed or ClientMailboxStatus.NotCreated)
        {
            mailbox.Status = ClientMailboxStatus.Creating;
            mailbox.LastError = null;
            ClientMailboxProvisioner.Audit(db, mailbox, ClientMailboxAuditAction.Retried, userContext.UserId);
        }
        else
        {
            return Result.Failure<ClientMailboxDto>(
                mailbox.Status == ClientMailboxStatus.Transferred ? MailboxErrors.Transferred : MailboxErrors.AlreadyExists);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ClientMailboxDtos.Of(mailbox);
    }
}

/// <summary>Jobul de fundal: pune în coadă dosarele ajunse la pas și creează ce e în coadă.</summary>
public sealed record ProcessClientMailboxesCommand : ICommand<int>;

internal sealed class ProcessClientMailboxesCommandHandler(IApplicationDbContext db, ClientMailboxProvisioner provisioner, IOptions<MailboxOptions> options)
    : ICommandHandler<ProcessClientMailboxesCommand, int>
{
    public async Task<Result<int>> Handle(ProcessClientMailboxesCommand command, CancellationToken cancellationToken)
    {
        // Cu Migadu neconfigurat, coada automată așteaptă. Ce cere adminul de mână rulează oricum,
        // ca eroarea de configurare să se vadă în dosar.
        if (options.Value.CanProvision)
        {
            await provisioner.EnqueueDueAsync(cancellationToken);
        }

        List<ClientMailbox> queued = await db.ClientMailboxes
            .Where(m => m.Status == ClientMailboxStatus.Creating)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (ClientMailbox mailbox in queued)
        {
            await provisioner.ProvisionAsync(mailbox, cancellationToken);
        }

        return queued.Count;
    }
}

/// <summary>
/// <c>POST /admin/onboarding/{id}/mailbox/credentials</c> — „Afișează credențiale RIDElance”.
/// POST, nu GET: fiecare afișare scrie în jurnal.
/// </summary>
public sealed record RevealOpsCredentialsCommand(Guid PfaId) : ICommand<OpsCredentialsDto>;

internal sealed class RevealOpsCredentialsCommandHandler(IApplicationDbContext db, IUserContext userContext, IMailboxCredentialProtector secrets)
    : ICommandHandler<RevealOpsCredentialsCommand, OpsCredentialsDto>
{
    public async Task<Result<OpsCredentialsDto>> Handle(RevealOpsCredentialsCommand command, CancellationToken cancellationToken)
    {
        ClientMailbox? mailbox = await ClientMailboxDtos.ForPfaAsync(db, command.PfaId, cancellationToken);
        if (mailbox is null)
        {
            return Result.Failure<OpsCredentialsDto>(MailboxErrors.NotCreated);
        }

        if (mailbox.Status != ClientMailboxStatus.Active || mailbox.OpsIdentityAddress is null || mailbox.OpsIdentityPasswordEncrypted is null)
        {
            return Result.Failure<OpsCredentialsDto>(
                mailbox.Status == ClientMailboxStatus.Transferred ? MailboxErrors.Transferred : MailboxErrors.NotActive);
        }

        ClientMailboxProvisioner.Audit(db, mailbox, ClientMailboxAuditAction.CredentialsRevealed, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        return new OpsCredentialsDto(mailbox.OpsIdentityAddress, secrets.Unprotect(mailbox.OpsIdentityPasswordEncrypted));
    }
}

/// <summary>
/// <c>POST /admin/onboarding/{id}/mailbox/transfer</c> — predarea către client (offboarding):
/// parolă nouă pe mailbox, emailul personal ca adresă de recuperare, identitatea RIDElance ștearsă,
/// documentul „Predare email” generat. Mailbox-ul rămâne, cu tot istoricul.
/// </summary>
public sealed record TransferClientMailboxCommand(Guid PfaId) : ICommand<ClientMailboxDto>;

internal sealed class TransferClientMailboxCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    IMailboxProvider provider,
    IMailboxCredentialProtector secrets,
    IRegisterExporter exporter,
    IFileEncryptionService files)
    : ICommandHandler<TransferClientMailboxCommand, ClientMailboxDto>
{
    public async Task<Result<ClientMailboxDto>> Handle(TransferClientMailboxCommand command, CancellationToken cancellationToken)
    {
        ClientMailbox? mailbox = await ClientMailboxDtos.ForPfaAsync(db, command.PfaId, cancellationToken);
        if (mailbox is null)
        {
            return Result.Failure<ClientMailboxDto>(MailboxErrors.NotCreated);
        }

        if (mailbox.Status != ClientMailboxStatus.Active || mailbox.LocalPart is null || mailbox.Address is null)
        {
            return Result.Failure<ClientMailboxDto>(
                mailbox.Status == ClientMailboxStatus.Transferred ? MailboxErrors.Transferred : MailboxErrors.NotActive);
        }

        var client = await db.Users.AsNoTracking()
            .Where(u => u.Id == mailbox.UserId)
            .Select(u => new { u.Email, u.FirstName, u.LastName })
            .SingleAsync(cancellationToken);

        string newPassword = MailboxPasswords.Generate();
        try
        {
            // Rotirea: parola stocată până acum nu mai deschide nimic. O salvăm imediat, ca o
            // întrerupere la pasul următor să nu lase baza de date cu o parolă care nu mai e bună.
            await provider.SetMailboxPasswordAsync(mailbox.LocalPart, newPassword, cancellationToken);
            mailbox.MailboxPasswordEncrypted = secrets.Protect(newPassword);
            await db.SaveChangesAsync(cancellationToken);

            await provider.SetRecoveryEmailAsync(mailbox.LocalPart, client.Email, cancellationToken);

            if (mailbox.OpsIdentityLocalPart is not null)
            {
                await provider.DeleteIdentityAsync(mailbox.LocalPart, mailbox.OpsIdentityLocalPart, cancellationToken);
            }
        }
        catch (MailboxProviderException exception)
        {
            // Rămâne activ: predarea se poate relua, fiecare pas se poate repeta fără stricăciuni.
            return Result.Failure<ClientMailboxDto>(MailboxErrors.Provider(exception.Message));
        }

        Document document = await StoreHandoverDocumentAsync(mailbox, $"{client.FirstName} {client.LastName}".Trim(), client.Email, newPassword, cancellationToken);

        mailbox.HandoverDocumentId = document.Id;
        mailbox.Status = ClientMailboxStatus.Transferred;
        mailbox.TransferredAtUtc = DateTime.UtcNow;
        // După predare parolele nu mai au ce căuta la noi: a clientului e în documentul lui, a
        // identității nu mai deschide nimic.
        mailbox.MailboxPasswordEncrypted = null;
        mailbox.OpsIdentityPasswordEncrypted = null;
        ClientMailboxProvisioner.Audit(db, mailbox, ClientMailboxAuditAction.Transferred, userContext.UserId, $"Recuperare: {client.Email}");
        await db.SaveChangesAsync(cancellationToken);

        return ClientMailboxDtos.Of(mailbox);
    }

    /// <summary>
    /// „Predare email”: adresa, parola nouă, setările de server și webmailul. Stocat criptat, ca
    /// document generat de noi (invizibil în lista clientului); intră în pachetul de predare PFA.
    /// </summary>
    private async Task<Document> StoreHandoverDocumentAsync(ClientMailbox mailbox, string clientName, string recoveryEmail, string password, CancellationToken cancellationToken)
    {
        byte[] pdf = exporter.ToPdf(MailboxHandoverDocument.Build(mailbox.Address!, password, clientName, recoveryEmail, DateOnly.FromDateTime(DateTime.UtcNow)));

        string storedFileName = $"{Guid.NewGuid()}.pdf";
        EncryptedFileResult encrypted;
        using (var stream = new MemoryStream(pdf))
        {
            encrypted = await files.EncryptAndSaveAsync(stream, storedFileName, cancellationToken);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = mailbox.UserId,
            PfaRegistrationId = mailbox.PfaRegistrationId,
            OriginalFileName = MailboxHandoverDocument.FileName,
            StoredFileName = storedFileName,
            ContentType = "application/pdf",
            Category = DocumentCategory.Other,
            Status = DocumentStatus.Verified,
            Origin = DocumentOrigin.SystemGenerated,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = pdf.Length,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };
        db.Documents.Add(document);
        return document;
    }
}

/// <summary>Documentul „Predare email”, în forma registrelor: aceeași foaie ca restul pachetului de predare.</summary>
public static class MailboxHandoverDocument
{
    public const string FileName = "Predare_email.pdf";

    public static RegisterDocument Build(string address, string password, string clientName, string recoveryEmail, DateOnly date) => new(
        "PREDARE EMAIL",
        "Adresa operațională RIDElance",
        [clientName, $"Predat la {date:dd.MM.yyyy}"],
        [new("Element", Width: 1.5f), new("Valoare", Width: 3f)],
        null,
        [
            new(["Adresă", address]),
            new(["Parolă", password]),
            new(["Webmail", MailboxServers.Webmail]),
            new(["IMAP (primire)", $"{MailboxServers.ImapHost}, port {MailboxServers.ImapPort}, {MailboxServers.Security}"]),
            new(["SMTP (trimitere)", $"{MailboxServers.SmtpHost}, port {MailboxServers.SmtpPort}, {MailboxServers.Security}"]),
            new(["Utilizator", address]),
            new(["Email de recuperare", recoveryEmail]),
        ],
        [
            "Adresa a fost folosită de RIDElance pentru ARR și conturile Uber/Bolt. Accesul RIDElance a fost retras la predare.",
            "Schimbă parola la prima autentificare, din webmail.",
        ]);
}

/// <summary><c>GET /pfa/mailbox</c> — adresa operațională a clientului logat, dacă există.</summary>
public sealed record GetOwnMailboxQuery : IQuery<OwnMailboxDto?>;

internal sealed class GetOwnMailboxQueryHandler(IApplicationDbContext db, IUserContext userContext) : IQueryHandler<GetOwnMailboxQuery, OwnMailboxDto?>
{
    public async Task<Result<OwnMailboxDto?>> Handle(GetOwnMailboxQuery query, CancellationToken cancellationToken)
    {
        string? address = await db.ClientMailboxes.AsNoTracking()
            .Where(m => m.UserId == userContext.UserId && (m.Status == ClientMailboxStatus.Active || m.Status == ClientMailboxStatus.Transferred))
            .Select(m => m.Address)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(address is null ? null : new OwnMailboxDto(address));
    }
}

/// <summary><c>GET /admin/mailboxes/usage</c> — emailurile de azi față de limitele planului.</summary>
public sealed record GetMailboxUsageQuery : IQuery<MailboxUsageDto>;

internal static class MailboxUsageRules
{
    /// <summary>De la 80% din oricare limită în sus e timpul de upgrade.</summary>
    public const decimal AlertShare = 0.8m;

    public static MailboxUsageDto Of(MailboxUsage usage, MailboxOptions options) => new(
        usage.IncomingToday,
        options.DailyIncomingLimit,
        usage.OutgoingToday,
        options.DailyOutgoingLimit,
        usage.StorageGb,
        Over(usage.IncomingToday, options.DailyIncomingLimit) || Over(usage.OutgoingToday, options.DailyOutgoingLimit));

    private static bool Over(int used, int limit) => limit > 0 && used >= limit * AlertShare;
}

internal sealed class GetMailboxUsageQueryHandler(IMailboxProvider provider, IOptions<MailboxOptions> options) : IQueryHandler<GetMailboxUsageQuery, MailboxUsageDto>
{
    public async Task<Result<MailboxUsageDto>> Handle(GetMailboxUsageQuery query, CancellationToken cancellationToken)
    {
        try
        {
            return MailboxUsageRules.Of(await provider.GetDomainUsageAsync(cancellationToken), options.Value);
        }
        catch (MailboxProviderException exception)
        {
            return Result.Failure<MailboxUsageDto>(MailboxErrors.Provider(exception.Message));
        }
    }
}

/// <summary>Jobul zilnic: citește consumul și anunță adminii peste 80% din oricare limită.</summary>
public sealed record CheckMailboxUsageCommand : ICommand<MailboxUsageDto>;

internal sealed class CheckMailboxUsageCommandHandler(IApplicationDbContext db, IMailboxProvider provider, IOptions<MailboxOptions> options)
    : ICommandHandler<CheckMailboxUsageCommand, MailboxUsageDto>
{
    public async Task<Result<MailboxUsageDto>> Handle(CheckMailboxUsageCommand command, CancellationToken cancellationToken)
    {
        MailboxUsageDto usage;
        try
        {
            usage = MailboxUsageRules.Of(await provider.GetDomainUsageAsync(cancellationToken), options.Value);
        }
        catch (MailboxProviderException exception)
        {
            return Result.Failure<MailboxUsageDto>(MailboxErrors.Provider(exception.Message));
        }

        if (!usage.Alert)
        {
            return usage;
        }

        // O alertă pe zi, nu una la fiecare citire.
        string dedupeKey = $"mailbox-usage:{DateTime.UtcNow:yyyy-MM-dd}";
        if (await db.Notifications.AnyAsync(n => n.DedupeKey == dedupeKey, cancellationToken))
        {
            return usage;
        }

        List<Guid> adminIds = await db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).ToListAsync(cancellationToken);
        foreach (Guid adminId in adminIds)
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                Text = $"Emailurile operaționale se apropie de limita planului Migadu: {usage.IncomingToday}/{usage.IncomingLimit} primite, " +
                       $"{usage.OutgoingToday}/{usage.OutgoingLimit} trimise azi. E timpul de upgrade.",
                Type = NotificationTypes.OnboardingStepAwaitingAdmin,
                DedupeKey = dedupeKey,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return usage;
    }
}

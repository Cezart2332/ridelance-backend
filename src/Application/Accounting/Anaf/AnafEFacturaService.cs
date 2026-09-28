using System.Security.Cryptography;
using Application.Abstractions.Anaf;
using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Accounting.Declarations;
using Domain.Accounting;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.Accounting.Anaf;

public static class AnafIntegrationErrors
{
    public static readonly Error NotConfigured = Error.Problem(
        "Anaf.NotConfigured",
        "Conexiunea ANAF nu e configurată (AnafOAuth:ClientId, AnafOAuth:ClientSecret).");

    public static readonly Error NotConnected = Error.Problem("Anaf.NotConnected", "Contul ANAF nu e conectat. Conectează-l cu certificatul.");

    public static readonly Error Expired = Error.Problem("Anaf.Expired", "Autorizarea ANAF a expirat. Conectează din nou contul ANAF.");

    public static readonly Error AuthorizationNotFound = Error.Problem(
        "Anaf.AuthorizationNotFound",
        "Autorizarea ANAF a expirat sau nu a pornit din aplicație. Încearcă din nou.");

    public static readonly Error InvalidCui = Error.Problem("Anaf.InvalidCui", "PFA-ul nu are un CUI valid.");

    public static readonly Error NotLinked = Error.Problem("Anaf.NotLinked", "Clientul nu e conectat la e-Factura.");

    public static readonly Error MessageNotFound = Error.NotFound("Anaf.MessageNotFound", "Mesajul e-Factura nu există.");

    public static readonly Error NotAnInvoice = Error.Problem("Anaf.NotAnInvoice", "Mesajul nu conține o factură.");

    public static Error Denied(string message) => Error.Problem("Anaf.Denied", $"ANAF: {message}");
}

public sealed record EFacturaSyncResult(int NewMessages, int Downloaded);

/// <summary>
/// Conexiunea ANAF a împuternicitului și e-Factura pe clienți. Adminul se conectează o dată cu
/// certificatul (OAuth); apoi, pentru fiecare PFA conectat, mesajele e-Factura (facturi primite,
/// trimise, erori) se listează, se descarcă și se citesc, fără ca cineva să intre în SPV.
/// </summary>
internal sealed class AnafEFacturaService(
    IApplicationDbContext db,
    IAnafEFacturaClient anaf,
    ISecretProtector secrets,
    DeclarationFiles files,
    ILogger<AnafEFacturaService> logger)
{
    /// <summary>ANAF păstrează mesajele 60 de zile; lista nu acceptă un interval mai lung.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromDays(60);

    /// <summary>Tokenul de acces se reînnoiește cu o săptămână înainte să expire.</summary>
    private static readonly TimeSpan RefreshAhead = TimeSpan.FromDays(7);

    /// <summary>Codul de autorizare ANAF e valabil 60 de secunde; cererea noastră, 10 minute.</summary>
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(10);

    private const int MaxDownloadsPerSync = 100;

    public bool IsConfigured => anaf.IsConfigured;

    public async Task<Result<string>> StartAsync(Guid userId, string? returnPath, CancellationToken cancellationToken)
    {
        if (!anaf.IsConfigured)
        {
            return Result.Failure<string>(AnafIntegrationErrors.NotConfigured);
        }

        string path = returnPath is { Length: > 0 } && returnPath.StartsWith('/') && !returnPath.StartsWith("//", StringComparison.Ordinal)
            ? returnPath
            : "/admin";
        var request = new AnafAuthorizationRequest
        {
            Id = Guid.NewGuid(),
            State = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            UserId = userId,
            ReturnPath = path,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.AnafAuthorizationRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return anaf.AuthorizeUrl(request.State).AbsoluteUri;
    }

    /// <summary>
    /// Callback-ul ANAF: schimbă codul pe tokenuri și le salvează ca noua conexiune. Întoarce calea
    /// din aplicație la care revine adminul. Fără <paramref name="state"/> (dacă ANAF nu-l trimite
    /// înapoi), se ia cea mai recentă autorizare pornită în ultimele 10 minute.
    /// </summary>
    public async Task<(string ReturnPath, Result Outcome)> CompleteAsync(string? code, string? state, string? anafError, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        IQueryable<AnafAuthorizationRequest> pending = db.AnafAuthorizationRequests
            .Where(r => r.CompletedAtUtc == null && r.CreatedAtUtc >= now - AuthorizationLifetime);
        AnafAuthorizationRequest? request = string.IsNullOrWhiteSpace(state)
            ? await pending.OrderByDescending(r => r.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken)
            : await pending.SingleOrDefaultAsync(r => r.State == state, cancellationToken);
        if (request is null)
        {
            return ("/admin", Result.Failure(AnafIntegrationErrors.AuthorizationNotFound));
        }

        request.CompletedAtUtc = now;
        if (string.IsNullOrWhiteSpace(code))
        {
            await db.SaveChangesAsync(cancellationToken);
            return (request.ReturnPath, Result.Failure(AnafIntegrationErrors.Denied(anafError ?? "autorizarea nu a fost acordată.")));
        }

        Result<AnafTokens> tokens = await anaf.ExchangeCodeAsync(code, cancellationToken);
        if (tokens.IsFailure)
        {
            await db.SaveChangesAsync(cancellationToken);
            return (request.ReturnPath, Result.Failure(tokens.Error));
        }

        foreach (AnafConnection old in await db.AnafConnections.Where(c => c.Status == AnafConnectionStatus.Active).ToListAsync(cancellationToken))
        {
            old.Status = AnafConnectionStatus.Disconnected;
            old.DisconnectedAtUtc = now;
        }

        db.AnafConnections.Add(new AnafConnection
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Status = AnafConnectionStatus.Active,
            AccessTokenProtected = secrets.Protect(tokens.Value.AccessToken),
            RefreshTokenProtected = secrets.Protect(tokens.Value.RefreshToken),
            AccessExpiresAtUtc = tokens.Value.AccessExpiresAtUtc,
            RefreshExpiresAtUtc = tokens.Value.RefreshExpiresAtUtc,
            ConnectedAtUtc = now,
        });
        AccountingAudit.Record(db, null, nameof(AnafConnection), request.Id, "CONNECT", null, new { request.UserId }, null, request.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return (request.ReturnPath, Result.Success());
    }

    public Task<AnafConnection?> ActiveConnectionAsync(CancellationToken cancellationToken) =>
        db.AnafConnections
            .Where(c => c.Status != AnafConnectionStatus.Disconnected)
            .OrderByDescending(c => c.ConnectedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task DisconnectAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (AnafConnection connection in await db.AnafConnections.Where(c => c.Status != AnafConnectionStatus.Disconnected).ToListAsync(cancellationToken))
        {
            connection.Status = AnafConnectionStatus.Disconnected;
            connection.DisconnectedAtUtc = DateTime.UtcNow;
            AccountingAudit.Record(db, null, nameof(AnafConnection), connection.Id, "DISCONNECT", null, null, null, userId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Tokenul de acces valabil, reînnoit dacă expiră în curând.</summary>
    public async Task<Result<string>> AccessTokenAsync(CancellationToken cancellationToken)
    {
        AnafConnection? connection = await ActiveConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return Result.Failure<string>(AnafIntegrationErrors.NotConnected);
        }

        if (connection.Status == AnafConnectionStatus.Expired)
        {
            return Result.Failure<string>(AnafIntegrationErrors.Expired);
        }

        DateTime now = DateTime.UtcNow;
        if (connection.AccessExpiresAtUtc - RefreshAhead > now)
        {
            return secrets.Unprotect(connection.AccessTokenProtected);
        }

        if (connection.RefreshExpiresAtUtc <= now)
        {
            connection.Status = AnafConnectionStatus.Expired;
            connection.LastError = AnafIntegrationErrors.Expired.Description;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure<string>(AnafIntegrationErrors.Expired);
        }

        Result<AnafTokens> refreshed = await anaf.RefreshAsync(secrets.Unprotect(connection.RefreshTokenProtected), cancellationToken);
        if (refreshed.IsFailure)
        {
            connection.LastError = refreshed.Error.Description;
            if (refreshed.Error.Code == "Anaf.TokenRejected" || connection.AccessExpiresAtUtc <= now)
            {
                connection.Status = AnafConnectionStatus.Expired;
            }

            await db.SaveChangesAsync(cancellationToken);
            return connection.Status == AnafConnectionStatus.Active
                ? secrets.Unprotect(connection.AccessTokenProtected)
                : Result.Failure<string>(refreshed.Error);
        }

        // ANAF rotește ambele tokenuri: cel vechi de refresh nu mai merge.
        connection.AccessTokenProtected = secrets.Protect(refreshed.Value.AccessToken);
        connection.RefreshTokenProtected = secrets.Protect(refreshed.Value.RefreshToken);
        connection.AccessExpiresAtUtc = refreshed.Value.AccessExpiresAtUtc;
        connection.RefreshExpiresAtUtc = refreshed.Value.RefreshExpiresAtUtc;
        connection.RefreshedAtUtc = now;
        connection.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        return refreshed.Value.AccessToken;
    }

    /// <summary>
    /// Conectează un PFA la e-Factura: verifică la ANAF că certificatul împuternicitului are drept pe
    /// CUI-ul lui, apoi face prima sincronizare. Fără drept, legătura rămâne cu motivul ANAF.
    /// </summary>
    public async Task<Result<AnafPfaLink>> ConnectPfaAsync(Guid pfaId, Guid userId, CancellationToken cancellationToken)
    {
        string? cui = await CuiAsync(pfaId, cancellationToken);
        if (cui is null)
        {
            return Result.Failure<AnafPfaLink>(AnafIntegrationErrors.InvalidCui);
        }

        Result<string> token = await AccessTokenAsync(cancellationToken);
        if (token.IsFailure)
        {
            return Result.Failure<AnafPfaLink>(token.Error);
        }

        DateTime now = DateTime.UtcNow;
        Result<EFacturaPage> probe = await anaf.ListMessagesAsync(token.Value, cui, now - Window + TimeSpan.FromMinutes(5), now, 1, cancellationToken);
        if (probe.IsFailure)
        {
            return Result.Failure<AnafPfaLink>(probe.Error);
        }

        AnafPfaLink? link = await db.AnafPfaLinks.SingleOrDefaultAsync(l => l.PfaRegistrationId == pfaId, cancellationToken);
        if (link is null)
        {
            link = new AnafPfaLink { Id = Guid.NewGuid(), PfaRegistrationId = pfaId };
            db.AnafPfaLinks.Add(link);
        }

        link.EnabledByUserId = userId;
        link.EnabledAtUtc = now;
        link.Status = probe.Value.Error is null ? AnafPfaLinkStatus.Active : AnafPfaLinkStatus.NoAccess;
        link.LastError = probe.Value.Error;
        AccountingAudit.Record(db, pfaId, nameof(AnafPfaLink), link.Id, "CONNECT", null, new { link.Status }, link.LastError, userId);
        await db.SaveChangesAsync(cancellationToken);

        if (link.Status == AnafPfaLinkStatus.Active)
        {
            await SyncAsync(pfaId, cancellationToken);
        }

        return link;
    }

    public async Task DisablePfaAsync(Guid pfaId, Guid userId, CancellationToken cancellationToken)
    {
        AnafPfaLink? link = await db.AnafPfaLinks.SingleOrDefaultAsync(l => l.PfaRegistrationId == pfaId, cancellationToken);
        if (link is null)
        {
            return;
        }

        link.Status = AnafPfaLinkStatus.Disabled;
        AccountingAudit.Record(db, pfaId, nameof(AnafPfaLink), link.Id, "DISABLE", null, null, null, userId);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Mesajele noi din ultimele 60 de zile, apoi arhivele lor (cel mult 100 pe rulare).</summary>
    public async Task<Result<EFacturaSyncResult>> SyncAsync(Guid pfaId, CancellationToken cancellationToken)
    {
        AnafPfaLink? link = await db.AnafPfaLinks.SingleOrDefaultAsync(l => l.PfaRegistrationId == pfaId && l.Status == AnafPfaLinkStatus.Active, cancellationToken);
        string? cui = await CuiAsync(pfaId, cancellationToken);
        if (link is null || cui is null)
        {
            return Result.Failure<EFacturaSyncResult>(link is null ? AnafIntegrationErrors.NotLinked : AnafIntegrationErrors.InvalidCui);
        }

        Result<string> token = await AccessTokenAsync(cancellationToken);
        if (token.IsFailure)
        {
            link.LastError = token.Error.Description;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure<EFacturaSyncResult>(token.Error);
        }

        DateTime now = DateTime.UtcNow;
        DateTime earliest = now - Window + TimeSpan.FromMinutes(5);
        DateTime from = link.LastSyncAtUtc is { } last && last - TimeSpan.FromDays(2) > earliest ? last - TimeSpan.FromDays(2) : earliest;

        HashSet<string> known = [.. await db.EFacturaMessages.Where(m => m.PfaRegistrationId == pfaId).Select(m => m.AnafMessageId).ToListAsync(cancellationToken)];
        int added = 0;
        int pages = 1;
        for (int page = 1; page <= pages; page++)
        {
            Result<EFacturaPage> listed = await anaf.ListMessagesAsync(token.Value, cui, from, now, page, cancellationToken);
            if (listed.IsFailure || listed.Value.Error is not null)
            {
                link.LastError = listed.IsFailure ? listed.Error.Description : $"ANAF: {listed.Value.Error}";
                await db.SaveChangesAsync(cancellationToken);
                return Result.Failure<EFacturaSyncResult>(listed.IsFailure ? listed.Error : AnafIntegrationErrors.Denied(listed.Value.Error!));
            }

            pages = Math.Max(1, listed.Value.TotalPages);
            foreach (EFacturaListItem item in listed.Value.Messages.Where(m => known.Add(m.Id)))
            {
                db.EFacturaMessages.Add(new EFacturaMessage
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = pfaId,
                    AnafMessageId = item.Id,
                    AnafType = item.Type,
                    Kind = KindOf(item.Type),
                    AnafCreatedAtUtc = item.CreatedAtUtc,
                    UploadId = item.UploadId,
                    Details = item.Details,
                    CreatedAtUtc = now,
                });
                added++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        int downloaded = 0;
        List<EFacturaMessage> pending = await db.EFacturaMessages
            .Where(m => m.PfaRegistrationId == pfaId && m.ZipDocumentId == null && m.DownloadError == null)
            .OrderBy(m => m.AnafCreatedAtUtc)
            .Take(MaxDownloadsPerSync)
            .ToListAsync(cancellationToken);
        foreach (EFacturaMessage message in pending)
        {
            Result<byte[]> zip = await anaf.DownloadAsync(token.Value, message.AnafMessageId, cancellationToken);
            if (zip.IsFailure)
            {
                message.DownloadError = zip.Error.Description;
                logger.LogWarning("e-Factura {MessageId}: descărcarea a eșuat: {Error}", message.AnafMessageId, zip.Error.Description);
                continue;
            }

            Document document = await files.StoreAsync(pfaId, zip.Value, $"efactura_{message.AnafMessageId}.zip", "application/zip", cancellationToken);
            message.ZipDocumentId = document.Id;
            message.DownloadedAtUtc = DateTime.UtcNow;
            if (EFacturaXml.MainXml(zip.Value) is { } xml && EFacturaXml.Read(xml) is { } invoice)
            {
                message.IsCreditNote = invoice.CreditNote;
                message.InvoiceNumber = invoice.Number;
                message.IssueDate = invoice.IssueDate;
                message.SupplierName = invoice.SupplierName;
                message.SupplierCif = invoice.SupplierCif;
                message.CustomerName = invoice.CustomerName;
                message.CustomerCif = invoice.CustomerCif;
                message.Currency = invoice.Currency;
                message.TotalAmount = invoice.Total;
                message.VatAmount = invoice.Vat;
            }

            downloaded++;
        }

        link.LastSyncAtUtc = now;
        link.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        return new EFacturaSyncResult(added, downloaded);
    }

    /// <summary>XML-ul facturii din arhiva ANAF.</summary>
    public async Task<Result<(byte[] Content, string FileName)>> XmlAsync(Guid messageId, CancellationToken cancellationToken)
    {
        EFacturaMessage? message = await db.EFacturaMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return Result.Failure<(byte[], string)>(AnafIntegrationErrors.MessageNotFound);
        }

        (byte[] Content, Document Document)? zip = await files.ReadAsync(message.ZipDocumentId, cancellationToken);
        return zip is { } archive && EFacturaXml.MainXml(archive.Content) is { } xml
            ? (xml, $"{FileBase(message)}.xml")
            : Result.Failure<(byte[], string)>(AnafIntegrationErrors.NotAnInvoice);
    }

    /// <summary>PDF-ul facturii: generat o dată de ANAF din XML, apoi păstrat.</summary>
    public async Task<Result<(byte[] Content, string FileName)>> PdfAsync(Guid messageId, CancellationToken cancellationToken)
    {
        EFacturaMessage? message = await db.EFacturaMessages.SingleOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (message is null)
        {
            return Result.Failure<(byte[], string)>(AnafIntegrationErrors.MessageNotFound);
        }

        if (await files.ReadAsync(message.PdfDocumentId, cancellationToken) is { } stored)
        {
            return (stored.Content, $"{FileBase(message)}.pdf");
        }

        if (message.Kind is not (EFacturaMessageKind.Received or EFacturaMessageKind.Sent))
        {
            return Result.Failure<(byte[], string)>(AnafIntegrationErrors.NotAnInvoice);
        }

        Result<(byte[] Content, string FileName)> xml = await XmlAsync(messageId, cancellationToken);
        if (xml.IsFailure)
        {
            return xml;
        }

        Result<byte[]> pdf = await anaf.ToPdfAsync(xml.Value.Content, message.IsCreditNote, cancellationToken);
        if (pdf.IsFailure)
        {
            return Result.Failure<(byte[], string)>(pdf.Error);
        }

        Document document = await files.StoreAsync(message.PfaRegistrationId, pdf.Value, $"{FileBase(message)}.pdf", "application/pdf", cancellationToken);
        message.PdfDocumentId = document.Id;
        await db.SaveChangesAsync(cancellationToken);
        return (pdf.Value, document.OriginalFileName);
    }

    private async Task<string?> CuiAsync(Guid pfaId, CancellationToken cancellationToken)
    {
        string? raw = await db.PfaRegistrations.Where(p => p.Id == pfaId).Select(p => p.Cui).SingleOrDefaultAsync(cancellationToken);
        string digits = new([.. (raw ?? string.Empty).Where(char.IsDigit)]);
        return digits.Length > 0 && Domain.PfaRegistrations.CuiValidator.Validate(digits).IsValid ? digits : null;
    }

    private static string FileBase(EFacturaMessage message)
    {
        string number = new([.. (message.InvoiceNumber ?? message.AnafMessageId).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')]);
        return $"efactura_{number}";
    }

    internal static EFacturaMessageKind KindOf(string type) => type.Trim().ToUpperInvariant() switch
    {
        "FACTURA PRIMITA" => EFacturaMessageKind.Received,
        "FACTURA TRIMISA" => EFacturaMessageKind.Sent,
        "ERORI FACTURA" => EFacturaMessageKind.Error,
        var other when other.StartsWith("MESAJ CUMPARATOR", StringComparison.Ordinal) => EFacturaMessageKind.BuyerMessage,
        _ => EFacturaMessageKind.Other,
    };
}

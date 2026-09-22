using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.ClientNotifications;

/// <summary>
/// Contabilul (sau adminul) trimite o notificare clientului PFA: apare în clopoțel și vine ca push.
/// </summary>
/// <param name="Destination">
/// Unde duce notificarea în aplicația clientului, una din <see cref="ClientNotificationDestinations"/>.
/// <c>null</c> deschide doar notificarea.
/// </param>
public sealed record SendClientNotificationCommand(
    Guid PfaRegistrationId,
    string Text,
    string? Destination) : ICommand<SendClientNotificationResponse>;

public sealed record SendClientNotificationResponse(Guid NotificationId, int PushSent);

/// <summary>Secțiunile din aplicația PFA către care poate trimite o notificare a contabilului.</summary>
public static class ClientNotificationDestinations
{
    public const string RecurringDocuments = "RecurringDocuments";
    public const string Documents = "Documents";
    public const string Taxes = "Taxes";
    public const string FiscalProfile = "FiscalProfile";
    public const string AccountantChat = "AccountantChat";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        RecurringDocuments,
        Documents,
        Taxes,
        FiscalProfile,
        AccountantChat,
    };
}

internal sealed class SendClientNotificationCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IWebPushService webPushService,
    IConfiguration configuration)
    : ICommandHandler<SendClientNotificationCommand, SendClientNotificationResponse>
{
    public const int MaxTextLength = 1000;
    public const string PushTitle = "Mesaj de la contabil";

    public async Task<Result<SendClientNotificationResponse>> Handle(
        SendClientNotificationCommand command,
        CancellationToken cancellationToken)
    {
        string text = command.Text?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return Result.Failure<SendClientNotificationResponse>(
                Error.Problem("ClientNotification.TextEmpty", "Scrie mesajul notificării."));
        }

        if (text.Length > MaxTextLength)
        {
            return Result.Failure<SendClientNotificationResponse>(
                Error.Problem("ClientNotification.TextTooLong", $"Mesajul poate avea cel mult {MaxTextLength} de caractere."));
        }

        if (command.Destination is not null && !ClientNotificationDestinations.All.Contains(command.Destination))
        {
            return Result.Failure<SendClientNotificationResponse>(
                Error.Problem("ClientNotification.UnknownDestination", "Secțiunea aleasă nu există."));
        }

        PfaRegistration? pfa = await context.PfaRegistrations
            .Include(p => p.User)
            .ThenInclude(u => u.PushSubscriptions)
            .SingleOrDefaultAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<SendClientNotificationResponse>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        bool allowed = caller is not null
            && (caller.Role is UserRole.Admin
                || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId);

        if (!allowed)
        {
            return Result.Failure<SendClientNotificationResponse>(
                Error.Failure("Pfa.AccessDenied", "Poți trimite notificări doar clienților tăi."));
        }

        DateTime nowUtc = DateTime.UtcNow;

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = pfa.UserId,
            Text = text,
            Type = NotificationTypes.AccountantMessage,
            SectionKey = command.Destination,
            RelatedUserId = userContext.UserId,
            IsRead = false,
            CreatedAtUtc = nowUtc,
        };
        context.Notifications.Add(notification);

        // În istoricul clientului, ca să se vadă ce i s-a cerut și când.
        context.PfaActivityLogs.Add(new PfaActivityLog
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            ActivityType = "ClientNotified",
            Description = $"Notificare trimisă clientului: {text}",
            CreatedAtUtc = nowUtc,
            PerformedByUserId = userContext.UserId,
        });

        await context.SaveChangesAsync(cancellationToken);

        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
        string relativePath = $"/app/notificari/{notification.Id}";
        string deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();

        int pushSent = 0;
        foreach (PushSubscription subscription in pfa.User.PushSubscriptions)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(subscription, PushTitle, text, deepLink, cancellationToken);
                pushSent++;
            }
            catch
            {
                // Notificarea din aplicație a plecat deja; un abonament push expirat nu o anulează.
            }
        }

        return new SendClientNotificationResponse(notification.Id, pushSent);
    }
}

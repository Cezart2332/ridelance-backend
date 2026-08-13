using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>
/// RL-02 — adminul alocă pachetul de semnături și închide pasul fiscal. Abia după asta se
/// deblochează pasul următor.
/// </summary>
public sealed record CompleteSignaturePacketCommand(
    Guid RegistrationId,
    Guid ReviewerUserId,
    SignatureProvider Provider,
    string? PackageName,
    int? SignatureCount,
    DateTime? ExpiresAtUtc,
    string? ProviderReference,
    string? AdminNote) : ICommand;

/// <summary>Adminul întoarce pasul la șofer, cu un motiv pe care acesta îl vede.</summary>
public sealed record RejectSignaturePacketCommand(
    Guid RegistrationId,
    Guid ReviewerUserId,
    string Reason,
    string? AdminNote) : ICommand;

internal sealed class CompleteSignaturePacketCommandHandler(
    IApplicationDbContext context,
    IWebPushService webPushService,
    IConfiguration configuration)
    : ICommandHandler<CompleteSignaturePacketCommand>
{
    public async Task<Result> Handle(CompleteSignaturePacketCommand command, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await SignatureReview.LoadAsync(context, command.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Result.Failure(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        DateTime nowUtc = DateTime.UtcNow;
        OnboardingSignaturePacket packet = SignatureReview.EnsurePacket(context, registration, nowUtc);
        string fromStatus = SignatureReview.StateOf(packet);

        packet.Provider = command.Provider;
        packet.PackageName = command.PackageName;
        packet.SignatureCount = command.SignatureCount;
        packet.ExpiresAtUtc = command.ExpiresAtUtc;
        packet.ProviderReference = command.ProviderReference;
        packet.AdminNote = command.AdminNote;
        packet.RejectionReason = null;
        packet.SignedAtUtc ??= nowUtc;
        packet.SentAtUtc ??= nowUtc;
        packet.Status = SignaturePacketStatus.Completed;
        packet.UpdatedAtUtc = nowUtc;

        SignatureReview.Audit(context, registration, fromStatus, "completed", command.ReviewerUserId, command.AdminNote, nowUtc);

        const string text = "Pachetul de semnături a fost alocat. Pasul „Fiscal, bancă & semnături” este finalizat.";
        context.Notifications.Add(SignatureReview.NotificationFor(registration.UserId, text, nowUtc));

        await context.SaveChangesAsync(cancellationToken);

        await SignatureReview.PushAsync(
            webPushService, configuration, registration.User, "Pas finalizat", text, cancellationToken);

        return Result.Success();
    }
}

internal sealed class RejectSignaturePacketCommandHandler(
    IApplicationDbContext context,
    IWebPushService webPushService,
    IConfiguration configuration)
    : ICommandHandler<RejectSignaturePacketCommand>
{
    public async Task<Result> Handle(RejectSignaturePacketCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(Step2Errors.RejectionReasonRequired);
        }

        PfaRegistration? registration = await SignatureReview.LoadAsync(context, command.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Result.Failure(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        DateTime nowUtc = DateTime.UtcNow;
        OnboardingSignaturePacket packet = SignatureReview.EnsurePacket(context, registration, nowUtc);
        string fromStatus = SignatureReview.StateOf(packet);

        // Trimiterea se anulează: pasul redevine al șoferului, iar butonul „Trimite pentru
        // verificare” trebuie să reapară.
        packet.SubmittedForReviewAtUtc = null;
        packet.RejectionReason = command.Reason.Trim();
        packet.AdminNote = command.AdminNote;
        packet.Status = SignaturePacketStatus.Rejected;
        packet.UpdatedAtUtc = nowUtc;

        SignatureReview.Audit(context, registration, fromStatus, "rejected", command.ReviewerUserId, command.Reason, nowUtc);

        string text = $"Pasul „Fiscal, bancă & semnături” a fost redeschis: {packet.RejectionReason}";
        context.Notifications.Add(SignatureReview.NotificationFor(registration.UserId, text, nowUtc));

        await context.SaveChangesAsync(cancellationToken);

        await SignatureReview.PushAsync(
            webPushService, configuration, registration.User, "Pas redeschis", text, cancellationToken);

        return Result.Success();
    }
}

/// <summary>Bucățile comune celor două tranziții de admin, ca să nu diverge una de cealaltă.</summary>
internal static class SignatureReview
{
    public static Task<PfaRegistration?> LoadAsync(
        IApplicationDbContext context,
        Guid registrationId,
        CancellationToken cancellationToken) =>
        context.PfaRegistrations
            .Include(r => r.SignaturePacket)
            .Include(r => r.User)
                .ThenInclude(u => u.PushSubscriptions)
            .SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken);

    public static OnboardingSignaturePacket EnsurePacket(
        IApplicationDbContext context,
        PfaRegistration registration,
        DateTime nowUtc)
    {
        if (registration.SignaturePacket is not null)
        {
            return registration.SignaturePacket;
        }

        // Adminul poate aloca pachetul și fără ca șoferul să fi apăsat „Trimite pentru verificare”.
        var packet = new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };

        context.OnboardingSignaturePackets.Add(packet);
        registration.SignaturePacket = packet;
        return packet;
    }

    /// <summary>Starea pasului fiscal înainte de tranziție, pentru urma din audit.</summary>
    public static string StateOf(OnboardingSignaturePacket packet) => packet.Status switch
    {
        SignaturePacketStatus.Completed => "completed",
        SignaturePacketStatus.Rejected => "rejected",
        _ when packet.SubmittedForReviewAtUtc is not null => "pending_admin",
        _ => "in_progress",
    };

    public static void Audit(
        IApplicationDbContext context,
        PfaRegistration registration,
        string fromStatus,
        string toStatus,
        Guid reviewerUserId,
        string? note,
        DateTime nowUtc) =>
        context.OnboardingStepAudits.Add(new OnboardingStepAudit
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            StepKey = OnboardingStepCatalog.WireKeyOf(OnboardingStepKey.Fiscal),
            FromStatus = fromStatus,
            ToStatus = toStatus,
            PerformedByUserId = reviewerUserId,
            Note = note,
            CreatedAtUtc = nowUtc,
        });

    public static Notification NotificationFor(Guid userId, string text, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Text = text,
        Type = NotificationTypes.OnboardingStepUpdate,
        IsRead = false,
        CreatedAtUtc = nowUtc,
    };

    public static async Task PushAsync(
        IWebPushService webPushService,
        IConfiguration configuration,
        User user,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase)
            ? parsedBase
            : null;
        string deepLink = appBaseUri is null ? "/onboarding/step2" : new Uri(appBaseUri, "/onboarding/step2").ToString();

        foreach (PushSubscription sub in user.PushSubscriptions)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(sub, title, body, deepLink, cancellationToken);
            }
            catch
            {
                // Un push picat nu are voie să pice tranziția, care e deja salvată.
            }
        }
    }
}

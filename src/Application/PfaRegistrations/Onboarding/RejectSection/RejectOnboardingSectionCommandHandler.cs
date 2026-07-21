using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.Notifications;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.RejectSection;

internal sealed class RejectOnboardingSectionCommandHandler(
    IApplicationDbContext context,
    IWebPushService webPushService,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    IConfiguration configuration)
    : ICommandHandler<RejectOnboardingSectionCommand>
{
    public async Task<Result> Handle(
        RejectOnboardingSectionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SectionKey == OnboardingSectionKey.Pfa)
        {
            return Result.Failure(OnboardingErrors.PfaSectionManagedViaRegistration);
        }

        if (string.IsNullOrWhiteSpace(command.Note))
        {
            return Result.Failure(Error.Failure(
                "Onboarding.NoteRequired",
                "Motivul respingerii este obligatoriu."));
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.OnboardingSections)
            .Include(r => r.User)
                .ThenInclude(u => u.PushSubscriptions)
            .SingleOrDefaultAsync(r => r.Id == command.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Result.Failure(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        OnboardingSectionApproval? section = registration.OnboardingSections
            .SingleOrDefault(s => s.SectionKey == command.SectionKey);

        if (section is null)
        {
            return Result.Failure(OnboardingErrors.SectionNotFound);
        }

        if (section.Status is not (OnboardingSectionStatus.AwaitingValidation or OnboardingSectionStatus.InProgress))
        {
            return Result.Failure(OnboardingErrors.NotAwaitingValidation);
        }

        section.Status = OnboardingSectionStatus.Rejected;
        section.Note = command.Note.Trim();
        section.ValidatedAtUtc = null;
        section.ValidatedByUserId = command.ReviewerUserId;

        string sectionLabel = OnboardingSectionCatalog.SectionLabel(command.SectionKey);
        string text = $"Secțiunea „{sectionLabel}” necesită modificări. Vezi mențiunile echipei.";

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = registration.UserId,
            Text = text,
            Type = NotificationTypes.OnboardingSectionUpdate,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
        string relativePath = $"/onboarding/sections/{command.SectionKey}";
        string deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();

        foreach (PushSubscription sub in registration.User.PushSubscriptions)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(
                    sub, "Secțiune de completat", text, deepLink, cancellationToken);
            }
            catch
            {
                // Ignore push sending failures
            }
        }

        if (!string.IsNullOrWhiteSpace(registration.User.Email))
        {
            string mjml = EmailTemplates.Notice(
                "Secțiune de completat",
                $"{registration.User.FirstName} {registration.User.LastName}".Trim(),
                [
                    $"Secțiunea „{sectionLabel}” din onboardingul tău RIDElance a fost respinsă de echipa noastră și necesită modificări.",
                    "Corectează documentele conform mențiunilor de mai jos și retrimite secțiunea spre validare.",
                ],
                section.Note,
                "Deschide secțiunea",
                appBaseUri is null
                    ? new Uri(relativePath, UriKind.Relative)
                    : new Uri(appBaseUri, relativePath));

            await emailService.SendEmailAsync(
                registration.User.Email,
                $"Secțiune de completat — {sectionLabel}",
                mjmlRenderer.Render(mjml),
                cancellationToken);
        }

        return Result.Success();
    }
}

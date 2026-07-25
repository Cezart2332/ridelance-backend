using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.ValidateSection;

internal sealed class ValidateOnboardingSectionCommandHandler(
    IApplicationDbContext context,
    IWebPushService webPushService,
    IConfiguration configuration)
    : ICommandHandler<ValidateOnboardingSectionCommand>
{
    public async Task<Result> Handle(
        ValidateOnboardingSectionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SectionKey == OnboardingSectionKey.Pfa)
        {
            return Result.Failure(OnboardingErrors.PfaSectionManagedViaRegistration);
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

        // Adminul poate valida și direct din InProgress (înainte ca userul să apese „Trimite”).
        if (section.Status is not (OnboardingSectionStatus.AwaitingValidation or OnboardingSectionStatus.InProgress))
        {
            return Result.Failure(OnboardingErrors.NotAwaitingValidation);
        }

        section.Status = OnboardingSectionStatus.Validated;
        section.ValidatedAtUtc = DateTime.UtcNow;
        section.ValidatedByUserId = command.ReviewerUserId;
        section.Note = null;

        // Deblochează secțiunea următoare
        OnboardingSectionKey? nextKey = OnboardingSectionCatalog.NextSection(command.SectionKey);
        string text;
        if (nextKey is OnboardingSectionKey next)
        {
            OnboardingSectionApproval? nextSection = registration.OnboardingSections
                .SingleOrDefault(s => s.SectionKey == next);

            if (nextSection is null)
            {
                context.OnboardingSectionApprovals.Add(new OnboardingSectionApproval
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = registration.Id,
                    SectionKey = next,
                    Status = OnboardingSectionStatus.InProgress,
                    CreatedAtUtc = DateTime.UtcNow,
                });
            }
            else if (nextSection.Status == OnboardingSectionStatus.Locked)
            {
                nextSection.Status = OnboardingSectionStatus.InProgress;
            }

            text = $"Secțiunea „{OnboardingSectionCatalog.SectionLabel(command.SectionKey)}” a fost validată! " +
                   $"Următorul pas: {OnboardingSectionCatalog.SectionLabel(next)}.";
        }
        else
        {
            text = $"Secțiunea „{OnboardingSectionCatalog.SectionLabel(command.SectionKey)}” a fost validată!";
        }

        // Înrolarea NU se produce aici — se declanșează abia când toți cei 6 pași sunt finalizați
        // (vezi OnboardingProgress.TryMarkCompleted, apelat din GetOnboardingStateQueryHandler).

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

        await SendPushAsync(registration.User, "Secțiune validată", text, "/onboarding", cancellationToken);

        return Result.Success();
    }

    private async Task SendPushAsync(
        User user,
        string title,
        string body,
        string relativePath,
        CancellationToken cancellationToken)
    {
        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
        string deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();

        foreach (PushSubscription sub in user.PushSubscriptions)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(sub, title, body, deepLink, cancellationToken);
            }
            catch
            {
                // Ignore push sending failures
            }
        }
    }
}

using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Eligibility;

/// <summary>
/// Adminul validează sau respinge pasul 1 (Eligibilitate).
/// </summary>
/// <remarks>
/// Pasul avea doar evaluarea automată, din datele citite de pe acte, deci n-avea buton în admin:
/// un dosar cu actele corecte, dar citite prost, rămânea neconfirmat oricât s-ar fi uitat cineva
/// peste el. Acum verdictul e al omului, iar datele extrase sunt doar ajutor.
/// </remarks>
public sealed record ReviewEligibilityCommand(
    Guid RegistrationId,
    Guid ReviewerUserId,
    bool Approve,
    string? Note) : ICommand;

internal sealed class ReviewEligibilityCommandHandler(IApplicationDbContext context)
    : ICommandHandler<ReviewEligibilityCommand>
{
    public async Task<Result> Handle(ReviewEligibilityCommand command, CancellationToken cancellationToken)
    {
        if (!command.Approve && string.IsNullOrWhiteSpace(command.Note))
        {
            return Result.Failure(Error.Failure("Onboarding.NoteRequired", "Motivul respingerii este obligatoriu."));
        }

        Guid? userId = await context.PfaRegistrations
            .Where(r => r.Id == command.RegistrationId)
            .Select(r => (Guid?)r.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return Result.Failure(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        DateTime nowUtc = DateTime.UtcNow;

        OnboardingEligibilityProfile? profile = await context.OnboardingEligibilityProfiles
            .SingleOrDefaultAsync(p => p.UserId == userId.Value, cancellationToken);

        // Profilul îl creează OCR-ul la prima încărcare. Dacă n-a apucat (acte citite prost),
        // adminul tot trebuie să poată da verdictul — tocmai atunci e nevoie de el.
        if (profile is null)
        {
            profile = new OnboardingEligibilityProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId.Value,
                CreatedAtUtc = nowUtc,
            };
            context.OnboardingEligibilityProfiles.Add(profile);
        }

        string text;
        if (command.Approve)
        {
            profile.AdminValidatedAtUtc = nowUtc;
            profile.AdminValidatedByUserId = command.ReviewerUserId;
            profile.AdminRejectedAtUtc = null;
            profile.AdminReviewNote = null;
            text = "Secțiunea „Eligibilitate” a fost validată!";
        }
        else
        {
            profile.AdminValidatedAtUtc = null;
            profile.AdminValidatedByUserId = null;
            profile.AdminRejectedAtUtc = nowUtc;
            profile.AdminReviewNote = command.Note!.Trim();
            text = $"Secțiunea „Eligibilitate” necesită modificări: {profile.AdminReviewNote}";
        }

        profile.UpdatedAtUtc = nowUtc;

        context.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Text = text,
            Type = NotificationTypes.OnboardingSectionUpdate,
            SectionKey = "Eligibility",
            IsRead = false,
            CreatedAtUtc = nowUtc,
        });

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

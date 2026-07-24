using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.TestSkip;

/// <summary>
/// DOAR PENTRU TESTARE — de șters. Avansează onboardingul cu un pas fără
/// documente și fără admin: PFA Pending/Rejected → Approved, apoi validează
/// pe rând secțiunile de documente și deblochează următoarea.
/// </summary>
internal sealed class SkipOnboardingStepCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SkipOnboardingStepCommand>
{
    private static readonly Error NoRegistration = Error.Problem(
        "Onboarding.TestSkip.NoRegistration",
        "Completează întâi formularul PFA — abia apoi se poate sări peste pași.");

    private static readonly Error AlreadyComplete = Error.Problem(
        "Onboarding.TestSkip.AlreadyComplete",
        "Toate secțiunile sunt deja validate.");

    private static readonly OnboardingSectionKey[] DocumentSections =
    [
        OnboardingSectionKey.AutorizatieTransport,
        OnboardingSectionKey.CopieConforma,
        OnboardingSectionKey.Vehicul,
    ];

    public async Task<Result> Handle(SkipOnboardingStepCommand command, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.OnboardingSections)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure(NoRegistration);
        }

        if (registration.Status != PfaRegistrationStatus.Approved)
        {
            registration.Status = PfaRegistrationStatus.Approved;
            registration.ReviewedAtUtc = DateTime.UtcNow;
            registration.ReviewNote = null;
            Unlock(registration, OnboardingSectionKey.AutorizatieTransport);

            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        foreach (OnboardingSectionKey key in DocumentSections)
        {
            OnboardingSectionApproval? row = registration.OnboardingSections
                .SingleOrDefault(s => s.SectionKey == key);

            if (row?.Status == OnboardingSectionStatus.Validated)
            {
                continue;
            }

            if (row is null)
            {
                row = new OnboardingSectionApproval
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = registration.Id,
                    SectionKey = key,
                    CreatedAtUtc = DateTime.UtcNow,
                };
                context.OnboardingSectionApprovals.Add(row);
            }

            row.Status = OnboardingSectionStatus.Validated;
            row.ValidatedAtUtc = DateTime.UtcNow;
            row.Note = null;

            if (OnboardingSectionCatalog.NextSection(key) is OnboardingSectionKey next)
            {
                Unlock(registration, next);
            }

            // Ultima secțiune validată → înrolare (aceeași poartă ca fluxul real).
            OnboardingProgress.TryMarkCompleted(registration, DateTime.UtcNow);

            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        return Result.Failure(AlreadyComplete);
    }

    private void Unlock(PfaRegistration registration, OnboardingSectionKey key)
    {
        OnboardingSectionApproval? row = registration.OnboardingSections
            .SingleOrDefault(s => s.SectionKey == key);

        if (row is null)
        {
            context.OnboardingSectionApprovals.Add(new OnboardingSectionApproval
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                SectionKey = key,
                Status = OnboardingSectionStatus.InProgress,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        else if (row.Status == OnboardingSectionStatus.Locked)
        {
            row.Status = OnboardingSectionStatus.InProgress;
        }
    }
}

using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.SubmitSection;

internal sealed class SubmitOnboardingSectionCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SubmitOnboardingSectionCommand>
{
    public async Task<Result> Handle(
        SubmitOnboardingSectionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.SectionKey == OnboardingSectionKey.Pfa)
        {
            return Result.Failure(OnboardingErrors.PfaSectionManagedViaRegistration);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.OnboardingSections)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        OnboardingSectionApproval? section = registration?.OnboardingSections
            .SingleOrDefault(s => s.SectionKey == command.SectionKey);

        if (registration is null || section is null)
        {
            return Result.Failure(OnboardingErrors.SectionNotFound);
        }

        if (section.Status == OnboardingSectionStatus.Locked)
        {
            return Result.Failure(OnboardingErrors.SectionLocked);
        }

        if (section.Status is not (OnboardingSectionStatus.InProgress or OnboardingSectionStatus.Rejected))
        {
            return Result.Failure(OnboardingErrors.SectionNotSubmittable);
        }

        List<DocumentCategory> uploadedCategories = await context.Documents
            .Where(d => d.UserId == command.UserId && d.Status != DocumentStatus.Rejected)
            .Select(d => d.Category)
            .Distinct()
            .ToListAsync(cancellationToken);

        var missing = OnboardingSectionCatalog.RequirementsFor(command.SectionKey)
            .Where(req => !req.AcceptedCategories.Any(uploadedCategories.Contains))
            .Select(req => req.Label)
            .ToList();

        if (missing.Count > 0)
        {
            return Result.Failure(OnboardingErrors.MissingDocuments(string.Join(", ", missing)));
        }

        section.Status = OnboardingSectionStatus.AwaitingValidation;
        section.SubmittedAtUtc = DateTime.UtcNow;
        section.Note = null;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

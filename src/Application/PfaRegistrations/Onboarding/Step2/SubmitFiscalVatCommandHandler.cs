using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal sealed class SubmitFiscalVatCommandHandler(
    IApplicationDbContext context,
    OnboardingStateService stateService)
    : ICommandHandler<SubmitFiscalVatCommand>
{
    public async Task<Result> Handle(SubmitFiscalVatCommand command, CancellationToken cancellationToken)
    {
        // Poarta RL-01: se scrie doar pe pasul activ. Prima verificare din handler —
        // altfel am valida conținutul unei cereri care oricum nu are voie să treacă.
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Fiscal, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure(guard.Error);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.FiscalProfile)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure(Step2Errors.NoRegistration);
        }

        // „Da” fără dovadă nu e o declarație, e o presupunere. Cerem certificatul/decizia ANAF
        // înainte să scriem codul special în profilul fiscal.
        Guid? proofDocumentId = null;
        if (command.VatAnswer == VatAnswer.Yes)
        {
            proofDocumentId = await context.Documents
                .Where(d => d.UserId == command.UserId
                    && d.Category == DocumentCategory.CertificatTvaIntracomunitar
                    && d.Status != DocumentStatus.Rejected)
                .OrderByDescending(d => d.UploadedAtUtc)
                .Select(d => (Guid?)d.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (proofDocumentId is null)
            {
                return Result.Failure(Step2Errors.VatProofMissing);
            }
        }

        DateTime nowUtc = DateTime.UtcNow;

        PfaFiscalProfile profile = registration.FiscalProfile ?? new PfaFiscalProfile
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };

        if (registration.FiscalProfile is null)
        {
            context.PfaFiscalProfiles.Add(profile);
        }

        bool hasIntraCommunityCode = command.VatAnswer == VatAnswer.Yes;

        profile.VatAnswer = command.VatAnswer;
        profile.VatRegistrationKind = hasIntraCommunityCode
            ? VatRegistrationKind.SpecialArticle317
            : VatRegistrationKind.None;
        // Codul special art. 317 nu face PFA-ul plătitor de TVA în țară.
        profile.IsVatPayer = false;
        // Aceeași declarație alimentează și panoul fiscal din admin, ca cele două să nu diverge.
        profile.SpecialVatCodeStatus = hasIntraCommunityCode
            ? PfaSpecialVatCodeStatus.Yes
            : PfaSpecialVatCodeStatus.No;
        profile.SpecialVatCodeDocumentId = proofDocumentId ?? profile.SpecialVatCodeDocumentId;
        profile.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

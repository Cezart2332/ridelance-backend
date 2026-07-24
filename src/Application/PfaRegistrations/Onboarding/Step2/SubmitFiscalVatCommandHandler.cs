using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal sealed class SubmitFiscalVatCommandHandler(IApplicationDbContext context)
    : ICommandHandler<SubmitFiscalVatCommand>
{
    public async Task<Result> Handle(SubmitFiscalVatCommand command, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.FiscalProfile)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure(Step2Errors.NoRegistration);
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

        profile.VatAnswer = command.VatAnswer;
        profile.VatRegistrationKind = command.VatRegistrationKind;
        profile.IsVatPayer = command.VatRegistrationKind is VatRegistrationKind.StandardVat;
        profile.UpdatedAtUtc = nowUtc;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

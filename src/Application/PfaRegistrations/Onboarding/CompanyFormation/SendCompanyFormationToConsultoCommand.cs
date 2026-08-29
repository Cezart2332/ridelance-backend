using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Payments;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>
/// Reluarea manuală a trimiterii spre Consulto, din admin.
///
/// Există pentru cazul în care traseul normal (webhook Stripe) a confirmat plata dar nu a putut
/// produce sau livra arhiva. NU e o scurtătură peste plată: poarta e aceeași
/// (<see cref="ConsultoDossierSender"/>), deci un dosar neplătit primește 409 și de aici.
/// </summary>
public sealed record SendCompanyFormationToConsultoCommand(Guid CompanyFormationRequestId) : ICommand;

internal sealed class SendCompanyFormationToConsultoCommandHandler(
    IApplicationDbContext context,
    ConsultoDossierSender sender)
    : ICommandHandler<SendCompanyFormationToConsultoCommand>
{
    public async Task<Result> Handle(
        SendCompanyFormationToConsultoCommand command,
        CancellationToken cancellationToken)
    {
        Guid? pfaRegistrationId = await context.CompanyFormationRequests
            .AsNoTracking()
            .Where(r => r.Id == command.CompanyFormationRequestId)
            .Select(r => (Guid?)r.PfaRegistrationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (pfaRegistrationId is null)
        {
            return Result.Failure(CompanyFormationErrors.NoRegistration);
        }

        // Suma e cea din catalog, nu una trimisă de client: la o reluare nu mai există sesiunea
        // Stripe din care venise, iar avansul e oricum fix.
        return await sender.SendAsync(
            pfaRegistrationId.Value,
            Pricing.RidelanceStart.OnboardingAdvanceBani,
            stripeEventId: null,
            cancellationToken);
    }
}

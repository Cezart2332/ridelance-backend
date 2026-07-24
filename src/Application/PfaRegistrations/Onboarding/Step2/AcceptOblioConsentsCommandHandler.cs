using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal sealed class AcceptOblioConsentsCommandHandler(IApplicationDbContext context)
    : ICommandHandler<AcceptOblioConsentsCommand, Step2OblioDto>
{
    public async Task<Result<Step2OblioDto>> Handle(
        AcceptOblioConsentsCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.OblioAccount)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<Step2OblioDto>(Step2Errors.NoRegistration);
        }

        DateTime nowUtc = DateTime.UtcNow;

        PfaOblioAccount account = registration.OblioAccount ?? new PfaOblioAccount
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };

        if (registration.OblioAccount is null)
        {
            context.PfaOblioAccounts.Add(account);
        }

        account.AccountEmail = command.AccountEmail;
        account.AccountCreationConsent = command.AccountCreationConsent;
        account.DataProcessingConsent = command.DataProcessingConsent;
        account.EInvoiceConsent = command.EInvoiceConsent;
        account.AutoInvoicingConsent = command.AutoInvoicingConsent;
        account.RidelanceManagementConsent = command.RidelanceManagementConsent;
        account.TermsAcceptedConsent = command.TermsAcceptedConsent;
        account.UpdatedAtUtc = nowUtc;

        account.ConsentsAcceptedAtUtc = account.AllConsentsAccepted
            ? account.ConsentsAcceptedAtUtc ?? nowUtc
            : null;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new Step2OblioDto(
            account.AccountEmail,
            account.AccountCreationConsent,
            account.DataProcessingConsent,
            account.EInvoiceConsent,
            account.AutoInvoicingConsent,
            account.RidelanceManagementConsent,
            account.TermsAcceptedConsent,
            account.AllConsentsAccepted,
            account.IntegrationStatus.ToString()));
    }
}

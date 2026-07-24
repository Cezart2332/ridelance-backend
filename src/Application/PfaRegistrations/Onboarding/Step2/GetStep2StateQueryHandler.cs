using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal sealed class GetStep2StateQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetStep2StateQuery, Step2StateResponse>
{
    public async Task<Result<Step2StateResponse>> Handle(
        GetStep2StateQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .AsNoTracking()
            .Include(r => r.FiscalProfile)
            .Include(r => r.BankAccountDeclaration)
            .Include(r => r.OblioAccount)
            .Include(r => r.SignaturePacket)
                .ThenInclude(p => p!.Documents)
            .Where(r => r.UserId == query.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Success(new Step2StateResponse(null, null, null, null, null));
        }

        Step2FiscalDto? fiscal = registration.FiscalProfile is { } fp
            ? new Step2FiscalDto(fp.VatAnswer.ToString(), fp.VatRegistrationKind.ToString())
            : null;

        Step2BankDto? bank = registration.BankAccountDeclaration is { } bd
            ? new Step2BankDto(
                bd.BankName,
                bd.IbanMasked,
                bd.ConfirmationDocumentId is not null,
                bd.OcrIbanMatches,
                bd.Source.ToString(),
                bd.Status.ToString())
            : null;

        Step2OblioDto? oblio = registration.OblioAccount is { } oa
            ? new Step2OblioDto(
                oa.AccountEmail,
                oa.AccountCreationConsent,
                oa.DataProcessingConsent,
                oa.EInvoiceConsent,
                oa.AutoInvoicingConsent,
                oa.RidelanceManagementConsent,
                oa.TermsAcceptedConsent,
                oa.AllConsentsAccepted,
                oa.IntegrationStatus.ToString())
            : null;

        Step2SignatureDto? signature = registration.SignaturePacket is { } sp
            ? new Step2SignatureDto(
                sp.Provider.ToString(),
                sp.Status.ToString(),
                sp.Documents
                    .Select(d => new Step2SignatureDocDto(d.Type.ToString(), d.Label, d.IsSigned))
                    .ToList())
            : null;

        return Result.Success(new Step2StateResponse(registration.Id, fiscal, bank, oblio, signature));
    }
}

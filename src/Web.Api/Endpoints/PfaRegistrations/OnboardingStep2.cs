using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.Step2;
using Domain.PfaRegistrations;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>Pasul 2 — TVA, semnături, cont bancar, cont Oblio.</summary>
internal sealed class OnboardingStep2 : IEndpoint
{
    public sealed record VatRequest(string VatAnswer);

    public sealed record BankRequest(string? BankName, string Iban, Guid? ConfirmationDocumentId);

    public sealed record OblioRequest(
        string? AccountEmail,
        bool AccountCreationConsent,
        bool DataProcessingConsent,
        bool EInvoiceConsent,
        bool AutoInvoicingConsent,
        bool RidelanceManagementConsent,
        bool TermsAcceptedConsent);

    public sealed record BankVerifyRequest(string Status, string? AdminNote);

    public sealed record OblioAdvanceRequest(string IntegrationStatus, string? AdminNote);

    public sealed record SignatureRequest(string Provider, string Status, string? ProviderReference, string? AdminNote);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Starea Pasului 2 pentru userul curent
        app.MapGet("onboarding/step2", async (
            IUserContext userContext,
            IQueryHandler<GetStep2StateQuery, Step2StateResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<Step2StateResponse> result =
                await handler.Handle(new GetStep2StateQuery(userContext.UserId), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // 2.1 — TVA
        app.MapPost("onboarding/step2/vat", async (
            VatRequest request,
            IUserContext userContext,
            ICommandHandler<SubmitFiscalVatCommand> handler,
            CancellationToken cancellationToken) =>
        {
            // Doar Da/Nu: „nu știu” nu mai e un răspuns acceptat, iar Unknown înseamnă
            // „încă nu a răspuns” — nu poate fi trimis ca declarație.
            if (!Enum.TryParse(request.VatAnswer, ignoreCase: true, out VatAnswer answer) ||
                answer is not (VatAnswer.Yes or VatAnswer.No))
            {
                return Results.BadRequest("Invalid VAT values.");
            }

            Result result = await handler.Handle(
                new SubmitFiscalVatCommand(userContext.UserId, answer), cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // 2.3 — cont bancar
        app.MapPost("onboarding/step2/bank", async (
            BankRequest request,
            IUserContext userContext,
            ICommandHandler<SubmitBankDeclarationCommand, Step2BankDto> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SubmitBankDeclarationCommand(
                userContext.UserId, request.BankName, request.Iban, request.ConfirmationDocumentId);

            Result<Step2BankDto> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // 2.4 — cont Oblio (consimțăminte)
        app.MapPost("onboarding/step2/oblio", async (
            OblioRequest request,
            IUserContext userContext,
            ICommandHandler<AcceptOblioConsentsCommand, Step2OblioDto> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new AcceptOblioConsentsCommand(
                userContext.UserId,
                request.AccountEmail,
                request.AccountCreationConsent,
                request.DataProcessingConsent,
                request.EInvoiceConsent,
                request.AutoInvoicingConsent,
                request.RidelanceManagementConsent,
                request.TermsAcceptedConsent);

            Result<Step2OblioDto> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // --- Admin: avans manual ---
        app.MapPut("pfa-registrations/{id:guid}/step2/bank", async (
            Guid id,
            BankVerifyRequest request,
            ICommandHandler<VerifyBankDeclarationCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Status, ignoreCase: true, out BankDeclarationStatus status) || !Enum.IsDefined(status))
            {
                return Results.BadRequest("Invalid bank status.");
            }

            Result result = await handler.Handle(
                new VerifyBankDeclarationCommand(id, status, request.AdminNote), cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

        app.MapPut("pfa-registrations/{id:guid}/step2/oblio", async (
            Guid id,
            OblioAdvanceRequest request,
            ICommandHandler<AdvanceOblioIntegrationCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.IntegrationStatus, ignoreCase: true, out OblioIntegrationStatus status) || !Enum.IsDefined(status))
            {
                return Results.BadRequest("Invalid Oblio status.");
            }

            Result result = await handler.Handle(
                new AdvanceOblioIntegrationCommand(id, status, request.AdminNote), cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);

        app.MapPut("pfa-registrations/{id:guid}/step2/signature", async (
            Guid id,
            SignatureRequest request,
            ICommandHandler<UpdateSignaturePacketCommand> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Provider, ignoreCase: true, out SignatureProvider provider) || !Enum.IsDefined(provider) ||
                !Enum.TryParse(request.Status, ignoreCase: true, out SignaturePacketStatus status) || !Enum.IsDefined(status))
            {
                return Results.BadRequest("Invalid signature values.");
            }

            Result result = await handler.Handle(
                new UpdateSignaturePacketCommand(id, provider, status, request.ProviderReference, request.AdminNote),
                cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission("pfa:manage")
        .WithTags(Tags.PfaRegistrations);
    }
}

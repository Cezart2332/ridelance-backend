using Application.Abstractions.Messaging;
using Application.Accounting.VatRegistration;
using Domain.Accounting;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Cererile D700 pentru codul de TVA art. 317: generare, validare ANAF, aprobare, cod primit.</summary>
internal sealed class VatRegistrationEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("vat-registrations", async (
            IQueryHandler<ListVatRegistrationsQuery, IReadOnlyList<VatRegistrationDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<VatRegistrationDto>> result = await handler.Handle(new ListVatRegistrationsQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("pfas/{pfaId:guid}/vat-registration", async (
            Guid pfaId,
            IQueryHandler<GetPfaVatRegistrationQuery, VatRegistrationDto?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<VatRegistrationDto?> result = await handler.Handle(new GetPfaVatRegistrationQuery(pfaId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("pfas/{pfaId:guid}/vat-registration", async (
            Guid pfaId,
            ICommandHandler<GenerateVatRegistrationCommand, VatRegistrationDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<VatRegistrationDto> result = await handler.Handle(new GenerateVatRegistrationCommand(pfaId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("vat-registrations/{id:guid}/validate", async (
            Guid id,
            ICommandHandler<ValidateVatRegistrationCommand, VatRegistrationDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<VatRegistrationDto> result = await handler.Handle(new ValidateVatRegistrationCommand(id), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("vat-registrations/{id:guid}/transitions", async (
            Guid id,
            VatRegistrationTransitionRequest request,
            ICommandHandler<TransitionVatRegistrationCommand, VatRegistrationDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<VatRegistrationDto> result = await handler.Handle(new TransitionVatRegistrationCommand(id, request.To, request.Note), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("vat-registrations/{id:guid}/vat-code", async (
            Guid id,
            [FromForm] string vatCode,
            [FromForm] DateOnly validFrom,
            IFormFile? file,
            ICommandHandler<RegisterVatCodeCommand, VatRegistrationDto> handler,
            CancellationToken cancellationToken) =>
        {
            VatCertificateFile? certificate = null;
            if (file is { Length: > 0 })
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, cancellationToken);
                certificate = new VatCertificateFile(file.FileName, file.ContentType, buffer.ToArray());
            }

            Result<VatRegistrationDto> result = await handler.Handle(new RegisterVatCodeCommand(id, vatCode, validFrom, certificate), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery();

        group.MapGet("vat-registrations/{id:guid}/xml", (Guid id, IQueryHandler<GetVatRegistrationFileQuery, VatRegistrationFile> handler, CancellationToken cancellationToken) =>
            File(id, VatRegistrationFileKind.Xml, handler, cancellationToken));
        group.MapGet("vat-registrations/{id:guid}/pdf", (Guid id, IQueryHandler<GetVatRegistrationFileQuery, VatRegistrationFile> handler, CancellationToken cancellationToken) =>
            File(id, VatRegistrationFileKind.Pdf, handler, cancellationToken));
        group.MapGet("vat-registrations/{id:guid}/certificate", (Guid id, IQueryHandler<GetVatRegistrationFileQuery, VatRegistrationFile> handler, CancellationToken cancellationToken) =>
            File(id, VatRegistrationFileKind.Certificate, handler, cancellationToken));
    }

    private static async Task<IResult> File(
        Guid id,
        VatRegistrationFileKind kind,
        IQueryHandler<GetVatRegistrationFileQuery, VatRegistrationFile> handler,
        CancellationToken cancellationToken)
    {
        Result<VatRegistrationFile> result = await handler.Handle(new GetVatRegistrationFileQuery(id, kind), cancellationToken);
        return result.Match(file => Results.File(file.Content, file.ContentType, file.FileName), CustomResults.Problem);
    }
}

/// <summary>Tranziția cerută: <c>APPROVED</c>, <c>REJECTED</c> (cu motiv) sau <c>SUBMITTED</c>.</summary>
internal sealed record VatRegistrationTransitionRequest(VatRegistrationStatus To, string? Note);

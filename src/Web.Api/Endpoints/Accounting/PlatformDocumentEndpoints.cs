using Application.Abstractions.Messaging;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Documentele Uber/Bolt ale unui PFA (spec contabilitate §4.2, B1).</summary>
internal sealed class PlatformDocumentEndpoints : IEndpoint
{
    public sealed record AssignInboxRequest(Guid PfaId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            // Același efect ca `.HasPermission(...)` al endpoint-urilor, aplicat pe tot grupul.
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("pfas/{pfaId:guid}/platform-documents", async (
            Guid pfaId,
            string period,
            IQueryHandler<ListPlatformDocumentsQuery, IReadOnlyList<PlatformDocumentListItem>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<PlatformDocumentListItem>> result = await handler.Handle(new ListPlatformDocumentsQuery(pfaId, period), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("pfas/{pfaId:guid}/platform-documents", async (
            Guid pfaId,
            [FromForm] IFormFile file,
            [FromForm] string period,
            ICommandHandler<UploadPlatformDocumentCommand, PlatformDocumentDto> handler,
            CancellationToken cancellationToken) =>
        {
            await using Stream stream = file.OpenReadStream();
            Result<PlatformDocumentDto> result = await handler.Handle(
                new UploadPlatformDocumentCommand(pfaId, period, file.FileName, file.ContentType, stream, file.Length),
                cancellationToken);

            // Duplicatul răspunde 409 cu documentul existent, ca ecranul să ofere „Deschide documentul existent”.
            if (result.IsFailure && result.Error is DuplicatePlatformDocumentError duplicate)
            {
                return Results.Problem(
                    title: duplicate.Code,
                    detail: duplicate.Description,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["existingDocumentId"] = duplicate.ExistingDocumentId });
            }

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery();

        // Încărcarea globală din „Clienți PFA”: alocare automată după CUI, numele fișierului sau comision.
        group.MapPost("platform-inbox", async (
            [FromForm] IFormFileCollection files,
            [FromForm] string period,
            ICommandHandler<UploadPlatformInboxCommand, IReadOnlyList<PlatformInboxResultDto>> handler,
            CancellationToken cancellationToken) =>
        {
            var uploads = new List<InboxUploadFile>();
            foreach (IFormFile file in files)
            {
                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, cancellationToken);
                uploads.Add(new InboxUploadFile(file.FileName, file.ContentType, buffer.ToArray()));
            }

            return (await handler.Handle(new UploadPlatformInboxCommand(period, uploads), cancellationToken)).Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery();

        group.MapGet("platform-inbox", async (bool? all, IQueryHandler<GetPlatformInboxQuery, IReadOnlyList<PlatformInboxItemDto>> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetPlatformInboxQuery(all != true), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("platform-inbox/{id:guid}/assign", async (Guid id, AssignInboxRequest request, ICommandHandler<AssignPlatformInboxItemCommand> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new AssignPlatformInboxItemCommand(id, request.PfaId), cancellationToken)).Match(Results.NoContent, CustomResults.Problem));

        group.MapPost("platform-inbox/{id:guid}/dismiss", async (Guid id, ICommandHandler<DismissPlatformInboxItemCommand> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new DismissPlatformInboxItemCommand(id), cancellationToken)).Match(Results.NoContent, CustomResults.Problem));

        group.MapGet("platform-documents/{id:guid}", async (
            Guid id,
            IQueryHandler<GetPlatformDocumentQuery, PlatformDocumentDetail> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PlatformDocumentDetail> result = await handler.Handle(new GetPlatformDocumentQuery(id), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("platform-documents/{id:guid}/file", async (
            Guid id,
            IQueryHandler<GetPlatformDocumentFileQuery, PlatformDocumentFile> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PlatformDocumentFile> result = await handler.Handle(new GetPlatformDocumentFileQuery(id), cancellationToken);
            return result.IsFailure
                ? CustomResults.Problem(result)
                : Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
        });

        group.MapPatch("platform-documents/{id:guid}/extraction", async (
            Guid id,
            UpdateExtractionRequest request,
            ICommandHandler<UpdateExtractionCommand, PlatformDocumentDetail> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PlatformDocumentDetail> result = await handler.Handle(new UpdateExtractionCommand(id, request.Fields, request.Reason), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("platform-documents/{id:guid}/confirm", async (
            Guid id,
            ICommandHandler<ConfirmPlatformDocumentCommand, PlatformDocumentDetail> handler,
            CancellationToken cancellationToken) =>
        {
            Result<PlatformDocumentDetail> result = await handler.Handle(new ConfirmPlatformDocumentCommand(id), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // Doar ADMIN: permisiunea de ștergere nu o are contabilul.
        group.MapDelete("platform-documents/{id:guid}", async (
            Guid id,
            ICommandHandler<DeletePlatformDocumentCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DeletePlatformDocumentCommand(id), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization(Permissions.DeleteAccountingDocuments);

        group.MapPost("platform-documents/confirm-bulk", async (
            ConfirmBulkRequest request,
            ICommandHandler<ConfirmPlatformDocumentsBulkCommand, ConfirmBulkResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ConfirmBulkResult> result = await handler.Handle(new ConfirmPlatformDocumentsBulkCommand(request.Ids), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }
}

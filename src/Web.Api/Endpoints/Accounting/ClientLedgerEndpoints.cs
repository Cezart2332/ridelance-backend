using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Ledger;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>
/// Tranzacțiile PFA-ului, pentru el însuși (spec flux contabil §8): lista cu statusuri, bonurile și
/// asocierile propuse. Fiecare rută lucrează pe PFA-ul utilizatorului logat, niciodată pe un id primit.
/// </summary>
internal sealed class ClientLedgerEndpoints : IEndpoint
{
    public sealed record ConfirmRequest(string Payment, Guid? LedgerEntryId, decimal? PersonalAmount);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("pfa/ledger")
            .RequireAuthorization()
            .WithTags(Tags.Accounting);

        // QA 11: taxele lunare de plată, din declarațiile generate (sumă, termen, stare).
        app.MapGet("pfa/declarations", async (
            int year,
            IQueryHandler<Application.Accounting.Declarations.GetClientDeclarationsQuery, IReadOnlyList<Application.Accounting.Declarations.ClientDeclarationDto>> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new Application.Accounting.Declarations.GetClientDeclarationsQuery(year), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
            .RequireAuthorization()
            .WithTags(Tags.Accounting);

        group.MapGet("transactions", async (
            DateOnly from,
            DateOnly to,
            IQueryHandler<GetClientTransactionsQuery, ClientTransactionsDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GetClientTransactionsQuery(from, to), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("expense-documents", async (
            [FromForm] IFormFile file,
            ICommandHandler<UploadClientExpenseDocumentCommand, ExpenseDocumentUploadResult> handler,
            CancellationToken cancellationToken) =>
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            Result<ExpenseDocumentUploadResult> result = await handler.Handle(
                new UploadClientExpenseDocumentCommand(new LedgerUpload(file.FileName, file.ContentType, buffer.ToArray())), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery();

        group.MapPost("expense-documents/{id:guid}/confirm", async (
            Guid id,
            ConfirmRequest request,
            ICommandHandler<ConfirmClientExpenseDocumentCommand, LedgerEntryDto> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Payment, ignoreCase: true, out ExpensePaymentChoice payment) || !Enum.IsDefined(payment))
            {
                return Results.Problem(title: "Accounting.InvalidPayment", detail: "Plata e BANK, CASH sau MANUAL.", statusCode: StatusCodes.Status400BadRequest);
            }

            Result<LedgerEntryDto> result = await handler.Handle(
                new ConfirmClientExpenseDocumentCommand(id, payment, request.LedgerEntryId, request.PersonalAmount), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("match-proposals/{id:guid}/accept", async (
            Guid id,
            ICommandHandler<ResolveClientMatchProposalCommand, LedgerEntryDto?> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new ResolveClientMatchProposalCommand(id, Accept: true), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("match-proposals/{id:guid}/reject", async (
            Guid id,
            ICommandHandler<ResolveClientMatchProposalCommand, LedgerEntryDto?> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new ResolveClientMatchProposalCommand(id, Accept: false), cancellationToken)).Match(_ => Results.NoContent(), CustomResults.Problem));
    }
}

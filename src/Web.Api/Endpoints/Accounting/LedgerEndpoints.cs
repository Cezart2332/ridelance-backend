using System.Text.Json;
using Application.Abstractions.Messaging;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>Ledger-ul unui PFA: listă, modificare, verificare, note manuale, documente, rapoarte Z (spec contabilitate §4.6, B6).</summary>
internal sealed class LedgerEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("pfas/{pfaId:guid}/ledger", async (
            Guid pfaId,
            DateOnly? from,
            DateOnly? to,
            string? status,
            string? type,
            string? source,
            int? page,
            int? pageSize,
            IQueryHandler<ListLedgerQuery, Paged<LedgerEntryDto>> handler,
            CancellationToken cancellationToken) =>
        {
            if (!TryParse(status, out LedgerEntryStatus? parsedStatus) ||
                !TryParse(type, out LedgerTransactionType? parsedType) ||
                !TryParse(source, out LedgerSource? parsedSource))
            {
                return Results.Problem(title: "Accounting.InvalidFilter", detail: "Filtrul nu are o valoare validă.", statusCode: StatusCodes.Status400BadRequest);
            }

            Result<Paged<LedgerEntryDto>> result = await handler.Handle(
                new ListLedgerQuery(pfaId, from, to, parsedStatus, parsedType, parsedSource, page ?? 1, pageSize ?? 25), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPatch("ledger/{id:guid}", async (
            Guid id,
            UpdateLedgerEntryRequest request,
            ICommandHandler<UpdateLedgerEntryCommand, LedgerEntryDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<LedgerEntryDto> result = await handler.Handle(new UpdateLedgerEntryCommand(id, request.Fields, request.Reason), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("ledger/{id:guid}/verify", async (
            Guid id,
            ICommandHandler<VerifyLedgerEntryCommand, LedgerEntryDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<LedgerEntryDto> result = await handler.Handle(new VerifyLedgerEntryCommand(id), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("pfas/{pfaId:guid}/ledger/manual", async (
            Guid pfaId,
            ManualLedgerEntryRequest request,
            ICommandHandler<CreateManualLedgerEntryCommand, LedgerEntryDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<LedgerEntryDto> result = await handler.Handle(new CreateManualLedgerEntryCommand(pfaId, request), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // Importul la cerere, în afara jobului zilnic (de ex. imediat după conectarea băncii).
        group.MapPost("pfas/{pfaId:guid}/ledger/import", async (
            Guid pfaId,
            ICommandHandler<RunLedgerImportCommand, IReadOnlyList<LedgerImportResult>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<LedgerImportResult>> result = await handler.Handle(new RunLedgerImportCommand(pfaId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("pfas/{pfaId:guid}/expense-documents", async (
            Guid pfaId,
            [FromForm] IFormFile file,
            ICommandHandler<UploadExpenseDocumentCommand, ExpenseDocumentUploadResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ExpenseDocumentUploadResult> result = await handler.Handle(
                new UploadExpenseDocumentCommand(pfaId, await ReadAsync(file, cancellationToken)), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery();

        // R30–R35: cum a fost plătit bonul (bancă / numerar / card neconectat) și partea personală.
        group.MapPost("pfas/{pfaId:guid}/expense-documents/{id:guid}/confirm", async (
            Guid pfaId,
            Guid id,
            ConfirmExpenseDocumentRequest request,
            ICommandHandler<ConfirmExpenseDocumentCommand, LedgerEntryDto> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Payment, ignoreCase: true, out ExpensePaymentChoice payment) || !Enum.IsDefined(payment))
            {
                return Results.Problem(title: "Accounting.InvalidPayment", detail: "Plata e BANK, CASH sau MANUAL.", statusCode: StatusCodes.Status400BadRequest);
            }

            Result<LedgerEntryDto> result = await handler.Handle(
                new ConfirmExpenseDocumentCommand(pfaId, id, payment, request.LedgerEntryId, request.PersonalAmount, request.Category), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // R36: plățile din bancă găsite pentru bonuri deja înregistrate.
        group.MapGet("pfas/{pfaId:guid}/match-proposals", async (
            Guid pfaId,
            IQueryHandler<ListMatchProposalsQuery, IReadOnlyList<MatchProposalDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<MatchProposalDto>> result = await handler.Handle(new ListMatchProposalsQuery(pfaId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("match-proposals/{id:guid}/accept", async (
            Guid id,
            ICommandHandler<ResolveMatchProposalCommand, LedgerEntryDto?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<LedgerEntryDto?> result = await handler.Handle(new ResolveMatchProposalCommand(id, Accept: true), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("match-proposals/{id:guid}/reject", async (
            Guid id,
            ICommandHandler<ResolveMatchProposalCommand, LedgerEntryDto?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<LedgerEntryDto?> result = await handler.Handle(new ResolveMatchProposalCommand(id, Accept: false), cancellationToken);
            return result.Match(_ => Results.NoContent(), CustomResults.Problem);
        });

        group.MapPost("pfas/{pfaId:guid}/z-reports", async (
            Guid pfaId,
            [FromForm] IFormFile file,
            ICommandHandler<UploadZReportCommand, ZReportUploadResult> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ZReportUploadResult> result = await handler.Handle(
                new UploadZReportCommand(pfaId, await ReadAsync(file, cancellationToken)), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery();
    }

    private static async Task<LedgerUpload> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return new LedgerUpload(file.FileName, file.ContentType, buffer.ToArray());
    }

    /// <summary>Filtrele vin în forma contractului (<c>NEEDS_REVIEW</c>, <c>CASH_Z</c>).</summary>
    private static bool TryParse<TEnum>(string? value, out TEnum? parsed)
        where TEnum : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        try
        {
            parsed = JsonSerializer.Deserialize<TEnum>(JsonSerializer.Serialize(value), AccountingJson.Options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

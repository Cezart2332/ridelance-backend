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

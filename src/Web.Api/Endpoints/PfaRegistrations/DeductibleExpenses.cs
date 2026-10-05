using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Expenses;
using Application.Expenses.Create;
using Application.Expenses.GetByPfa;
using Application.Expenses.Update;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class DeductibleExpenses : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("pfa-registrations/{id:guid}/deductible-expenses/sync-sources", async (
            Guid id, ICommandHandler<SyncExpenseSourcesCommand, IReadOnlyList<string>> handler, CancellationToken ct) =>
        {
            Result<IReadOnlyList<string>> result = await handler.Handle(new SyncExpenseSourcesCommand(id), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization().WithTags(Tags.PfaRegistrations);
        app.MapGet("pfa-registrations/{id:guid}/deductible-expenses/{expenseId:guid}/suggestion", async (
            Guid id, Guid expenseId, decimal? total, IQueryHandler<GetExpenseSuggestionQuery, ExpenseSuggestion> handler, CancellationToken ct) =>
        {
            Result<ExpenseSuggestion> result = await handler.Handle(new GetExpenseSuggestionQuery(id, expenseId, total), ct);
            return result.Match(Results.Ok, CustomResults.Problem);
        }).RequireAuthorization().WithTags(Tags.PfaRegistrations);
        app.MapGet("pfa-registrations/{id:guid}/deductible-expenses", async (
            Guid id,
            int? year,
            int? month,
            IQueryHandler<GetDeductibleExpensesByPfaQuery, List<DeductibleExpenseResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetDeductibleExpensesByPfaQuery(id, year, month);
            Result<List<DeductibleExpenseResponse>> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("pfa-registrations/{id:guid}/deductible-expenses", async (
            Guid id,
            [FromForm] IFormFile file,
            [FromForm] string catalogCategory,
            [FromForm] string itemName,
            [FromForm] string deductibleLabel,
            [FromForm] decimal? amountRon,
            [FromForm] int year,
            [FromForm] int month,
            [FromForm] DateOnly? expenseDate,
            [FromForm] string? supplierName,
            [FromForm] decimal? vatAmount,
            [FromForm] string? documentTypeLabel,
            ICommandHandler<CreateDeductibleExpenseCommand, DeductibleExpenseResponse> handler,
            CancellationToken cancellationToken) =>
        {
            await using Stream stream = file.OpenReadStream();

            var command = new CreateDeductibleExpenseCommand(
                id,
                catalogCategory,
                itemName,
                deductibleLabel,
                amountRon,
                year,
                month,
                file.FileName,
                file.ContentType,
                stream,
                file.Length,
                expenseDate,
                supplierName,
                vatAmount,
                documentTypeLabel);

            Result<DeductibleExpenseResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .DisableAntiforgery()
        .WithTags(Tags.PfaRegistrations);

        // Confirmarea de după OCR: omul corectează ce s-a citit greșit și decide dacă intră
        // în calcul. JSON, nu form — aici nu se mai încarcă niciun fișier.
        app.MapPut("pfa-registrations/{id:guid}/deductible-expenses/{expenseId:guid}", async (
            Guid id,
            Guid expenseId,
            UpdateDeductibleExpenseRequest request,
            ICommandHandler<UpdateDeductibleExpenseCommand, DeductibleExpenseResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdateDeductibleExpenseCommand(
                id,
                expenseId,
                request.CatalogCategory,
                request.ItemName,
                request.DeductibleLabel,
                request.AmountRon,
                request.Year,
                request.Month,
                request.ExpenseDate,
                request.SupplierName,
                request.VatAmount,
                request.DocumentTypeLabel,
                request.Confirm,
                request.PaymentMethod, request.PaymentDate, request.LedgerEntryId, request.AccountingCategory,
                request.PersonalAmount, request.DocumentNumber, request.Reason, request.ApproveDocument);

            Result<DeductibleExpenseResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);
    }

    internal sealed record UpdateDeductibleExpenseRequest(
        string CatalogCategory,
        string ItemName,
        string DeductibleLabel,
        decimal? AmountRon,
        int Year,
        int Month,
        DateOnly? ExpenseDate,
        string? SupplierName,
        decimal? VatAmount,
        string? DocumentTypeLabel,
        bool Confirm,
        string? PaymentMethod = null,
        DateOnly? PaymentDate = null,
        Guid? LedgerEntryId = null,
        string? AccountingCategory = null,
        decimal PersonalAmount = 0,
        string? DocumentNumber = null,
        string? Reason = null,
        bool ApproveDocument = false);
}

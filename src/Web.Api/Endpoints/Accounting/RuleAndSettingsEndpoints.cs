using System.Text.Json;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Ledger;
using Application.Accounting.Pfas;
using Application.Accounting.Rules;
using Application.FiscalEstimates;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>
/// Lista PFA-urilor, setările contabile, casa de marcat (§4.1), regulile fiscale (§4.5) și
/// preferința de numerar a PFA-ului din onboarding.
/// </summary>
internal sealed class RuleAndSettingsEndpoints : IEndpoint
{
    private static readonly Dictionary<string, TaxRuleKind> RulePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["suppliers"] = TaxRuleKind.Suppliers,
        ["vat-rates"] = TaxRuleKind.VatRates,
        ["d100"] = TaxRuleKind.D100,
        ["anaf-schemas"] = TaxRuleKind.AnafSchemas,
        ["expense-categories"] = TaxRuleKind.ExpenseCategories,
    };

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("pfas", async (string? status, string? search, IQueryHandler<ListPfasQuery, IReadOnlyList<PfaListItem>> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ListPfasQuery(status, search), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("clients", async (string period, IQueryHandler<ListClientWorkspaceQuery, IReadOnlyList<ClientWorkspaceRow>> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ListClientWorkspaceQuery(period), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("fiscal-overview", async (int year, IQueryHandler<ListFiscalOverviewQuery, FiscalOverviewDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new ListFiscalOverviewQuery(year), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("pfas/{pfaId:guid}/settings", async (Guid pfaId, IQueryHandler<GetPfaSettingsQuery, PfaAccountingSettingsDto> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetPfaSettingsQuery(pfaId), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPut("pfas/{pfaId:guid}/settings", async (
            Guid pfaId,
            SettingsChange change,
            ICommandHandler<UpdatePfaSettingsCommand, PfaAccountingSettingsDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new UpdatePfaSettingsCommand(pfaId, change), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("pfas/{pfaId:guid}/cash/evidence", async (
            Guid pfaId,
            [FromForm] IFormFile file,
            ICommandHandler<UploadCashEvidenceCommand, CashEvidenceUploadResult> handler,
            CancellationToken cancellationToken) =>
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            Result<CashEvidenceUploadResult> result = await handler.Handle(
                new UploadCashEvidenceCommand(pfaId, new LedgerUpload(file.FileName, file.ContentType, buffer.ToArray())), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .DisableAntiforgery();

        group.MapPost("pfas/{pfaId:guid}/cash/transition", async (
            Guid pfaId,
            CashTransitionRequest request,
            ICommandHandler<TransitionCashCommand, CashRegisterStateDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new TransitionCashCommand(pfaId, request), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("rules/exchange-rates", async (string currency, DateOnly date, IQueryHandler<GetExchangeRateQuery, ExchangeRateDto?> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetExchangeRateQuery(currency, date), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapGet("rules/{kind}", async (string kind, IQueryHandler<ListTaxRulesQuery, IReadOnlyList<object>> handler, CancellationToken cancellationToken) =>
            RulePaths.TryGetValue(kind, out TaxRuleKind parsed)
                ? (await handler.Handle(new ListTaxRulesQuery(parsed), cancellationToken)).Match(Results.Ok, CustomResults.Problem)
                : Results.NotFound());

        group.MapPost("rules/{kind}", async (string kind, JsonElement input, ICommandHandler<SaveTaxRuleCommand, object> handler, CancellationToken cancellationToken) =>
            RulePaths.TryGetValue(kind, out TaxRuleKind parsed)
                ? (await handler.Handle(new SaveTaxRuleCommand(parsed, null, input), cancellationToken)).Match(Results.Ok, CustomResults.Problem)
                : Results.NotFound());

        group.MapPut("rules/{kind}/{id:guid}", async (string kind, Guid id, JsonElement input, ICommandHandler<SaveTaxRuleCommand, object> handler, CancellationToken cancellationToken) =>
            RulePaths.TryGetValue(kind, out TaxRuleKind parsed)
                ? (await handler.Handle(new SaveTaxRuleCommand(parsed, id, input), cancellationToken)).Match(Results.Ok, CustomResults.Problem)
                : Results.NotFound());

        group.MapDelete("rules/suppliers/{id:guid}", async (Guid id, ICommandHandler<DeleteSupplierCommand> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new DeleteSupplierCommand(id), cancellationToken)).Match(Results.NoContent, CustomResults.Problem));

        // Rolul PFA: utilizatorul își vede și își schimbă propriul răspuns (nu cere drept de contabilitate).
        RouteGroupBuilder me = app.MapGroup("accounting/me")
            .RequireAuthorization()
            .WithTags(Tags.Accounting);

        me.MapGet("cash-preference", async (IQueryHandler<GetMyCashPreferenceQuery, CashPreference?> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetMyCashPreferenceQuery(), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        me.MapPut("cash-preference", async (CashPreferenceRequest request, ICommandHandler<SetMyCashPreferenceCommand, CashPreference> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new SetMyCashPreferenceCommand(request.CashRequested), cancellationToken)).Match(Results.Ok, CustomResults.Problem));
    }
}

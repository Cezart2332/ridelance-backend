using Application.Abstractions.Messaging;
using Application.Accounting.Annual;
using Domain.Accounting;
using Infrastructure.Authorization;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>
/// Declarațiile anuale (spec declarații F30–F61): ecranul anual al Adminului, contractele de chirie,
/// C801 și, pentru PFA, întrebarea despre alte venituri.
/// </summary>
internal sealed class AnnualEndpoints : IEndpoint
{
    public sealed record AnswersRequest(bool? HasExternalIncome, bool SupplementCompleted, decimal? AnafPrefilledNetIncome);

    public sealed record RentalContractRequest(
        string OwnerName, string OwnerCnp, string ContractNumber, DateOnly ContractDate, decimal GrossRent, string PaymentFrequency, Guid WithholdingRuleId);

    public sealed record RentPaymentRequest(DateOnly PaymentDate, decimal GrossAmount);

    public sealed record C801Request(C801Status Status, Guid? DocumentId, string? NuiNumber, string? VehiclePlate);

    public sealed record ExternalIncomeRequest(bool HasExternalIncome);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("accounting")
            .RequireAuthorization(Permissions.ManageAccounting)
            .WithTags(Tags.Accounting);

        group.MapGet("pfas/{pfaId:guid}/annual/{year:int}", async (
            Guid pfaId,
            int year,
            IQueryHandler<GetAnnualDeclarationsQuery, AnnualDeclarationsDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GetAnnualDeclarationsQuery(pfaId, year), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("pfas/{pfaId:guid}/annual/{year:int}/generate", async (
            Guid pfaId,
            int year,
            ICommandHandler<GenerateAnnualDeclarationsCommand, AnnualDeclarationsDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GenerateAnnualDeclarationsCommand(pfaId, year), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPut("pfas/{pfaId:guid}/annual/{year:int}/answers", async (
            Guid pfaId,
            int year,
            AnswersRequest request,
            ICommandHandler<SaveAnnualTaxAnswersCommand> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new SaveAnnualTaxAnswersCommand(pfaId, year, request.HasExternalIncome, request.SupplementCompleted, request.AnafPrefilledNetIncome), cancellationToken))
                .Match(Results.NoContent, CustomResults.Problem));

        group.MapGet("pfas/{pfaId:guid}/rental-contracts", async (
            Guid pfaId,
            IQueryHandler<ListRentalContractsQuery, IReadOnlyList<RentalContractDto>> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new ListRentalContractsQuery(pfaId), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPost("pfas/{pfaId:guid}/rental-contracts", async (
            Guid pfaId,
            RentalContractRequest request,
            ICommandHandler<CreateRentalContractCommand, Guid> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(
                new CreateRentalContractCommand(pfaId, request.OwnerName, request.OwnerCnp, request.ContractNumber, request.ContractDate, request.GrossRent, request.PaymentFrequency, request.WithholdingRuleId),
                cancellationToken)).Match(id => Results.Ok(new { id }), CustomResults.Problem));

        group.MapPost("rental-contracts/{id:guid}/payments", async (
            Guid id,
            RentPaymentRequest request,
            ICommandHandler<AddRentPaymentCommand, Guid> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new AddRentPaymentCommand(id, request.PaymentDate, request.GrossAmount), cancellationToken)).Match(paymentId => Results.Ok(new { id = paymentId }), CustomResults.Problem));

        group.MapGet("pfas/{pfaId:guid}/c801", async (
            Guid pfaId,
            IQueryHandler<GetC801Query, C801Dto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GetC801Query(pfaId), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        group.MapPut("pfas/{pfaId:guid}/c801", async (
            Guid pfaId,
            C801Request request,
            ICommandHandler<UpdateC801Command, C801Dto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new UpdateC801Command(pfaId, request.Status, request.DocumentId, request.NuiNumber, request.VehiclePlate), cancellationToken))
                .Match(Results.Ok, CustomResults.Problem));

        // PFA-ul, pentru el însuși: întrebarea anuală despre alte venituri (F53).
        RouteGroupBuilder client = app.MapGroup("pfa/annual")
            .RequireAuthorization()
            .WithTags(Tags.Accounting);

        client.MapGet("{year:int}", async (
            int year,
            IQueryHandler<GetClientAnnualQuery, ClientAnnualDto> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GetClientAnnualQuery(year), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        client.MapPut("{year:int}/external-income", async (
            int year,
            ExternalIncomeRequest request,
            ICommandHandler<AnswerExternalIncomeCommand> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new AnswerExternalIncomeCommand(year, request.HasExternalIncome), cancellationToken)).Match(Results.NoContent, CustomResults.Problem));
    }
}

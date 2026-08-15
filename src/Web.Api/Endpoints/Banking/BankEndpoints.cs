using Application.Abstractions.Messaging;
using Application.Banking.Commands;
using Application.Banking.Queries;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

#pragma warning disable IDE0007, IDE0008

namespace Web.Api.Endpoints.Banking;

internal sealed class BankEndpoints : IEndpoint
{
    /// <param name="InstitutionId">Null lasă alegerea băncii în ecranul providerului.</param>
    public sealed record InitiateConnectionRequest(string? InstitutionId);

    public sealed record ChooseConnectionRequest(string ProviderConnectionId);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("bank")
            .RequireAuthorization()
            .WithTags("Bank");

        group.MapGet("institutions", async (
            IQueryHandler<GetBankInstitutionsQuery, List<BankInstitutionResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<BankInstitutionResponse>> result =
                await handler.Handle(new GetBankInstitutionsQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("connection", async (
            IQueryHandler<GetBankConnectionQuery, BankConnectionResponse?> handler,
            CancellationToken cancellationToken) =>
        {
            Result<BankConnectionResponse?> result =
                await handler.Handle(new GetBankConnectionQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost("connection", async (
            InitiateConnectionRequest request,
            ICommandHandler<InitiateBankConnectionCommand, InitiateBankConnectionResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<InitiateBankConnectionResponse> result = await handler.Handle(
                new InitiateBankConnectionCommand(request.InstitutionId),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // Cazul ambiguu al revendicării: utilizatorul spune care conexiune e a lui.
        group.MapPost("connection/choose", async (
            ChooseConnectionRequest request,
            ICommandHandler<ChooseBankConnectionCommand, BankConnectionResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<BankConnectionResponse> result = await handler.Handle(
                new ChooseBankConnectionCommand(request.ProviderConnectionId),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("transactions", async (
            int? year,
            int? month,
            int? page,
            int? pageSize,
            IQueryHandler<GetBankTransactionsQuery, BankTransactionsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<BankTransactionsResponse> result = await handler.Handle(
                new GetBankTransactionsQuery(year, month, page ?? 1, pageSize ?? 25),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapDelete("connection", async (
            ICommandHandler<DisconnectBankConnectionCommand, bool> handler,
            CancellationToken cancellationToken) =>
        {
            Result<bool> result = await handler.Handle(
                new DisconnectBankConnectionCommand(),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }
}

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
    /// <param name="BankCode">Banca aleasă, cu codul din lista furnizorului.</param>
    /// <param name="PsuId">Numele de utilizator la bancă, unde banca îl cere.</param>
    /// <param name="PsuIdType">„PF" sau „PJ", unde banca face diferența.</param>
    /// <param name="PsuCorporateId">Codul de client firmă, unde e nevoie și de el.</param>
    /// <param name="TcAccepted">Bifa de termeni și condiții a serviciului de open banking.</param>
    public sealed record InitiateConnectionRequest(
        string BankCode,
        string? PsuId,
        string? PsuIdType,
        string? PsuCorporateId,
        string? Iban,
        bool TcAccepted);

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
            HttpContext httpContext,
            ICommandHandler<InitiateBankConnectionCommand, InitiateBankConnectionResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<InitiateBankConnectionResponse> result = await handler.Handle(
                new InitiateBankConnectionCommand(
                    request.BankCode,
                    request.PsuId,
                    request.PsuIdType,
                    request.PsuCorporateId,
                    request.Iban,
                    request.TcAccepted,
                    // Furnizorul cere IP-ul utilizatorului real, nu al serverului: fără el, banca
                    // ne limitează la patru interogări pe zi. Îl citim aici, nu în handler —
                    // stratul de aplicație n-are de ce să știe de HTTP.
                    httpContext.Connection.RemoteIpAddress?.ToString()),
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

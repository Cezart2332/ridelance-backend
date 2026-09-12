using System.Net;
using System.Net.Sockets;
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

    /// <summary>
    /// IP-ul clientului, în forma pe care o acceptă Smart Accounts: IPv4 punctat, atât.
    ///
    /// Două lucruri stricau asta în producție, dar nu și local:
    ///
    /// Socketul Kestrel e dual-stack, deci un client IPv4 apare ca IPv6 mapat
    /// (<c>::ffff:10.0.1.23</c>). Trimis așa, furnizorul răspunde 400 cu
    /// „INVALID FORMAT PSU-IP-ADDRESS" — verificat pe sandbox, pe toate băncile.
    ///
    /// Și în spatele unui proxy, adresa conexiunii e a proxy-ului, nu a omului. Adevărata adresă
    /// vine în <c>X-Forwarded-For</c>, prima din listă.
    ///
    /// Antetul e pus de proxy și poate fi falsificat de client, dar aici nu decide nimic: se duce
    /// mai departe la bancă, unde ține doar de limitarea numărului de interogări.
    /// </summary>
    private static string? PsuIpAddress(HttpContext httpContext)
    {
        string? forwarded = httpContext.Request.Headers["X-Forwarded-For"]
            .FirstOrDefault()?
            .Split(',')[0]
            .Trim();

        return Ipv4(forwarded) ?? Ipv4(httpContext.Connection.RemoteIpAddress?.ToString());
    }

    private static string? Ipv4(string? candidate)
    {
        if (!IPAddress.TryParse(candidate, out IPAddress? address))
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetwork ? address.ToString() : null;
    }

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
                    // Furnizorul cere IP-ul utilizatorului real, nu al serverului. Îl citim aici,
                    // nu în handler — stratul de aplicație n-are de ce să știe de HTTP.
                    PsuIpAddress(httpContext)),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // `from`/`to` sunt date calendaristice, inclusive la ambele capete; `userId` e pentru
        // contabil, care își vede clientul (dreptul se verifică în handler, nu aici).
        group.MapGet("transactions", async (
            DateOnly? from,
            DateOnly? to,
            int? page,
            int? pageSize,
            Guid? userId,
            IQueryHandler<GetBankTransactionsQuery, BankTransactionsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<BankTransactionsResponse> result = await handler.Handle(
                new GetBankTransactionsQuery(from, to, page ?? 1, pageSize ?? 25, userId),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("activity", async (
            DateOnly from,
            DateOnly to,
            string? bucket,
            Guid? userId,
            IQueryHandler<GetBankActivityQuery, BankActivityResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<BankActivityResponse> result = await handler.Handle(
                new GetBankActivityQuery(from, to, bucket ?? "day", userId),
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

using System.Net;
using System.Net.Sockets;
using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>
/// Ramura „Nu am PFA" — dosarul de înființare a societății prin partener. Etapele 1 și 2 se
/// salvează ca draft la fiecare ieșire dintr-un câmp; etapa 3 (consimțământ + semnătură) se
/// trimite atomic, ca să nu existe acorduri „pe jumătate date".
/// </summary>
internal sealed class OnboardingCompanyFormation : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("onboarding/company-formation")
            .RequireAuthorization()
            .WithTags(Tags.PfaRegistrations);

        // Starea completă a dosarului (resume).
        group.MapGet(string.Empty, async (
            IUserContext userContext,
            IQueryHandler<GetCompanyFormationQuery, CompanyFormationResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CompanyFormationResponse> result =
                await handler.Handle(new GetCompanyFormationQuery(userContext.UserId), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // Etapa 1 — datele solicitantului (autosave).
        group.MapPut("personal-data", async (
            PersoanaFizicaPayload payload,
            IUserContext userContext,
            ICommandHandler<SubmitPersonalDataCommand, CompanyFormationResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CompanyFormationResponse> result = await handler.Handle(
                new SubmitPersonalDataCommand(userContext.UserId, payload), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // Etapa 2 — sediul social, cu tot cu lista de proprietari (autosave).
        group.MapPut("registered-office", async (
            RegisteredOfficePayload payload,
            IUserContext userContext,
            ICommandHandler<SubmitRegisteredOfficeCommand, CompanyFormationResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CompanyFormationResponse> result = await handler.Handle(
                new SubmitRegisteredOfficeCommand(userContext.UserId, payload), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // Etapa 3 — consimțămintele și semnătura, atomic.
        group.MapPost("sign", async (
            SignaturePayload payload,
            HttpContext httpContext,
            IUserContext userContext,
            ICommandHandler<SignCompanyFormationCommand, CompanyFormationResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var signatureContext = new SignatureContext(
                ClientIpAddress(httpContext),
                httpContext.Request.Headers.UserAgent.ToString(),
                httpContext.Request.Headers["Idempotency-Key"].ToString());

            Result<CompanyFormationResponse> result = await handler.Handle(
                new SignCompanyFormationCommand(userContext.UserId, payload, signatureContext),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });

        // Textele wizardului, versionate. Publice pentru orice user autentificat.
        app.MapGet("legal/consent-flow", async (
            string context,
            IQueryHandler<GetLegalConsentFlowQuery, LegalConsentFlowDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<LegalConsentFlowDto> result =
                await handler.Handle(new GetLegalConsentFlowQuery(context), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);

        // Lista de adrese Consulto stă în afara dosarului: e un catalog, nu date ale userului.
        app.MapGet("onboarding/sedii-disponibile", async (
            IQueryHandler<GetConsultoOfficesQuery, IReadOnlyList<ConsultoOfficeDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<ConsultoOfficeDto>> result =
                await handler.Handle(new GetConsultoOfficesQuery(), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);
    }

    /// <summary>
    /// IP-ul semnatarului: primul IP public din <c>X-Forwarded-For</c>, altfel adresa conexiunii.
    /// Proxy-urile adaugă la coadă, deci primul element e clientul — dar poate fi o adresă
    /// privată dacă lanțul trece prin rețeaua internă.
    /// </summary>
    private static string? ClientIpAddress(HttpContext httpContext)
    {
        string forwarded = httpContext.Request.Headers["X-Forwarded-For"].ToString();

        foreach (string candidate in forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(candidate, out IPAddress? parsed) && !IsPrivate(parsed))
            {
                return parsed.ToString();
            }
        }

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        }

        byte[] octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            127 => true,
            169 => octets[1] == 254,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,
            _ => false,
        };
    }
}

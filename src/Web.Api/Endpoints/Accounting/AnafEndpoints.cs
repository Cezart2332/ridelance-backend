using Application.Abstractions.Messaging;
using Application.Accounting.Anaf;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Accounting;

/// <summary>
/// Conexiunea ANAF (OAuth cu certificatul împuternicitului) și e-Factura pe clienți. Conectarea și
/// sincronizarea sunt doar pentru admin; mesajele și facturile le vede și contabilul.
/// </summary>
internal sealed class AnafEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder anaf = app.MapGroup("anaf").RequireAuthorization(Permissions.ManageAnaf).WithTags(Tags.Accounting);

        anaf.MapPost("oauth/start", async (StartRequest request, ICommandHandler<StartAnafAuthorizationCommand, string> handler, CancellationToken cancellationToken) =>
        {
            Result<string> result = await handler.Handle(new StartAnafAuthorizationCommand(request.ReturnPath), cancellationToken);
            return result.Match(url => Results.Ok(new { url }), CustomResults.Problem);
        });

        anaf.MapGet("connection", async (IQueryHandler<GetAnafConnectionQuery, AnafConnectionDto> handler, CancellationToken cancellationToken) =>
        {
            Result<AnafConnectionDto> result = await handler.Handle(new GetAnafConnectionQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        anaf.MapDelete("connection", async (ICommandHandler<DisconnectAnafCommand> handler, CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DisconnectAnafCommand(), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        });

        // Callback-ul înregistrat la ANAF (https://api.ridelance.ro/anaf/oauth/callback). Vine din
        // browserul adminului, redirecționat de logincert.anaf.ro, deci fără tokenul aplicației: îl
        // identifică autorizarea pornită din aplicație. Rezultatul se întoarce în pagina de unde a plecat.
        app.MapGet("anaf/oauth/callback", async (
            string? code,
            string? state,
            string? error,
            [FromQuery(Name = "error_description")] string? errorDescription,
            ICommandHandler<CompleteAnafAuthorizationCommand, AnafAuthorizationOutcome> handler,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            string? authorizationError = error;
            if (!string.IsNullOrWhiteSpace(errorDescription))
            {
                authorizationError = string.IsNullOrWhiteSpace(error) ? errorDescription : $"{error}: {errorDescription}";
            }

            Result<AnafAuthorizationOutcome> result = await handler.Handle(new CompleteAnafAuthorizationCommand(code, state, authorizationError), cancellationToken);
            string baseUrl = (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
            AnafAuthorizationOutcome outcome = result.IsSuccess ? result.Value : new AnafAuthorizationOutcome("/admin", result.Error.Description);
            string separator = outcome.ReturnPath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            string flag = outcome.Error is null ? "anaf=conectat" : $"anaf_eroare={Uri.EscapeDataString(outcome.Error)}";
            return Results.Redirect($"{baseUrl}{outcome.ReturnPath}{separator}{flag}");
        })
        .AllowAnonymous()
        .WithTags(Tags.Accounting);

        RouteGroupBuilder view = app.MapGroup("accounting").RequireAuthorization(Permissions.ManageAccounting).WithTags(Tags.Accounting);

        view.MapGet("pfas/{pfaId:guid}/efactura", async (Guid pfaId, IQueryHandler<GetPfaEFacturaQuery, PfaEFacturaDto> handler, CancellationToken cancellationToken) =>
        {
            Result<PfaEFacturaDto> result = await handler.Handle(new GetPfaEFacturaQuery(pfaId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        view.MapGet("efactura/{id:guid}/xml", (Guid id, IQueryHandler<GetEFacturaFileQuery, AnafFile> handler, CancellationToken cancellationToken) =>
            File(id, EFacturaFileKind.Xml, handler, cancellationToken));
        view.MapGet("efactura/{id:guid}/pdf", (Guid id, IQueryHandler<GetEFacturaFileQuery, AnafFile> handler, CancellationToken cancellationToken) =>
            File(id, EFacturaFileKind.Pdf, handler, cancellationToken));

        RouteGroupBuilder manage = app.MapGroup("accounting").RequireAuthorization(Permissions.ManageAnaf).WithTags(Tags.Accounting);

        manage.MapPost("pfas/{pfaId:guid}/efactura/connect", async (Guid pfaId, ICommandHandler<ConnectPfaEFacturaCommand> handler, CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new ConnectPfaEFacturaCommand(pfaId), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        });

        manage.MapPost("pfas/{pfaId:guid}/efactura/sync", async (Guid pfaId, ICommandHandler<SyncPfaEFacturaCommand, EFacturaSyncResult> handler, CancellationToken cancellationToken) =>
        {
            Result<EFacturaSyncResult> result = await handler.Handle(new SyncPfaEFacturaCommand(pfaId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        manage.MapDelete("pfas/{pfaId:guid}/efactura", async (Guid pfaId, ICommandHandler<DisablePfaEFacturaCommand> handler, CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DisablePfaEFacturaCommand(pfaId), cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        });
    }

    private static async Task<IResult> File(Guid id, EFacturaFileKind kind, IQueryHandler<GetEFacturaFileQuery, AnafFile> handler, CancellationToken cancellationToken)
    {
        Result<AnafFile> result = await handler.Handle(new GetEFacturaFileQuery(id, kind), cancellationToken);
        return result.Match(file => Results.File(file.Content, file.ContentType, file.FileName), CustomResults.Problem);
    }

    internal sealed record StartRequest(string? ReturnPath);
}

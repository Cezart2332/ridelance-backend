using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

/// <summary>
/// Vederea de operator peste dosarul de înființare: datele cu CNP-ul mascat, probatoriul
/// semnăturii, dezvăluirea CNP-ului cu audit, redeschiderea dosarului și exportul pentru
/// Consulto.
/// </summary>
internal sealed class AdminCompanyFormation : IEndpoint
{
    public sealed record RevealCnpRequest(Guid? OwnerId);
    public sealed record RequestInfoRequest(string Reason);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("admin/company-formation/{id:guid}")
            .RequireAuthorization()
            .WithTags(Tags.PfaRegistrations);

        group.MapGet(string.Empty, async (
            Guid id,
            IQueryHandler<GetAdminCompanyFormationQuery, AdminCompanyFormationResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminCompanyFormationResponse> result =
                await handler.Handle(new GetAdminCompanyFormationQuery(id), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .HasPermission(Permissions.ManagePfaRegistrations);

        // POST, nu GET: fiecare dezvăluire de CNP lasă urmă în jurnalul dosarului.
        group.MapPost("reveal-cnp", async (
            Guid id,
            RevealCnpRequest request,
            IUserContext userContext,
            ICommandHandler<RevealCompanyFormationCnpCommand, RevealedCnpResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<RevealedCnpResponse> result = await handler.Handle(
                new RevealCompanyFormationCnpCommand(id, request.OwnerId, userContext.UserId),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .HasPermission(Permissions.ManagePfaRegistrations);

        group.MapPost("request-info", async (
            Guid id,
            RequestInfoRequest request,
            IUserContext userContext,
            ICommandHandler<RequestCompanyFormationInfoCommand, AdminCompanyFormationResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminCompanyFormationResponse> result = await handler.Handle(
                new RequestCompanyFormationInfoCommand(id, request.Reason, userContext.UserId),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .HasPermission(Permissions.ManagePfaRegistrations);

        // Reluarea trimiterii, când traseul normal (webhook Stripe) a confirmat plata dar nu a
        // putut livra arhiva. Poarta e aceeași: fără plată confirmată se întoarce 409.
        group.MapPost("send-to-consulto", async (
            Guid id,
            ICommandHandler<SendCompanyFormationToConsultoCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(
                new SendCompanyFormationToConsultoCommand(id), cancellationToken);

            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .HasPermission(Permissions.ManagePfaRegistrations);

        group.MapGet("export", async (
            Guid id,
            IQueryHandler<ExportCompanyFormationQuery, CompanyFormationExport> handler,
            CancellationToken cancellationToken) =>
        {
            Result<CompanyFormationExport> result =
                await handler.Handle(new ExportCompanyFormationQuery(id), cancellationToken);

            return result.Match(
                export => Results.File(export.Content, "application/zip", export.FileName),
                CustomResults.Problem);
        })
        .HasPermission(Permissions.ManagePfaRegistrations);
    }
}

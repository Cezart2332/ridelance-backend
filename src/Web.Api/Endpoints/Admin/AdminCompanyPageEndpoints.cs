using Application.Abstractions.Messaging;
using Application.Companies.Admin;
using Application.Companies.Commands.UpdateCompanyPage;
using Domain.Companies;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

/// <summary>
/// Moderarea mini-site-urilor firmelor.
/// </summary>
/// <remarks>
/// Rutele astea sunt singurul loc din care o pagină de firmă ajunge publică. Proprietarul scrie
/// ciorna prin <c>companies/profile/page</c>, dar ea rămâne o ciornă până când cineva de aici
/// apasă „aprobă" — vezi <c>Application/Companies/Page/CompanyPageReview.cs</c>.
///
/// Spre deosebire de rutele proprietarului, astea poartă id de profil în URL: adminul lucrează pe
/// firma altcuiva, deci ținta nu poate veni din contextul de autentificare.
/// </remarks>
internal sealed class AdminCompanyPageEndpoints : IEndpoint
{
    public sealed record ReviewRequest(string Decision, string? Note, IReadOnlyList<string>? BlockedSections);
    public sealed record SectionsRequest(IReadOnlyList<string>? BlockedSections, string? Note);

    public sealed record EditRequest(
        string? Tagline,
        string? PublicDescription,
        CompanyPageTheme? Theme,
        CompanyPageContent? Content,
        PickupLocationInput? Pickup);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/company-pages", async (
            string? status,
            string? search,
            IQueryHandler<GetAdminCompanyPagesQuery, IReadOnlyList<AdminCompanyPageListItem>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<AdminCompanyPageListItem>> result =
                await handler.Handle(new GetAdminCompanyPagesQuery(status, search), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageCompanyPages)
        .WithTags(Tags.Admin);

        app.MapGet("admin/company-pages/{id:guid}", async (
            Guid id,
            IQueryHandler<GetAdminCompanyPageQuery, AdminCompanyPageDetail> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminCompanyPageDetail> result =
                await handler.Handle(new GetAdminCompanyPageQuery(id), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageCompanyPages)
        .WithTags(Tags.Admin);

        // Aprobă sau refuză versiunea curentă a paginii.
        app.MapPost("admin/company-pages/{id:guid}/review", async (
            Guid id,
            ReviewRequest request,
            ICommandHandler<ReviewCompanyPageCommand, AdminCompanyPageDetail> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ReviewCompanyPageCommand(id, request.Decision, request.Note, request.BlockedSections);
            Result<AdminCompanyPageDetail> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageCompanyPages)
        .WithTags(Tags.Admin);

        // Pornește sau oprește secțiuni, fără să schimbe verdictul.
        app.MapPut("admin/company-pages/{id:guid}/sections", async (
            Guid id,
            SectionsRequest request,
            ICommandHandler<SetCompanyPageSectionsCommand, AdminCompanyPageDetail> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SetCompanyPageSectionsCommand(id, request.BlockedSections, request.Note);
            Result<AdminCompanyPageDetail> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageCompanyPages)
        .WithTags(Tags.Admin);

        // Corectează ciorna. Nu publică nimic — publicarea rămâne un pas separat.
        app.MapPut("admin/company-pages/{id:guid}/content", async (
            Guid id,
            EditRequest request,
            ICommandHandler<EditCompanyPageAsAdminCommand, AdminCompanyPageDetail> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new EditCompanyPageAsAdminCommand(
                id, request.Tagline, request.PublicDescription, request.Theme, request.Content, request.Pickup);

            Result<AdminCompanyPageDetail> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageCompanyPages)
        .WithTags(Tags.Admin);

        app.MapDelete("admin/company-pages/{id:guid}/cover", async (
            Guid id,
            ICommandHandler<RemoveCompanyPageCoverCommand, AdminCompanyPageDetail> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminCompanyPageDetail> result =
                await handler.Handle(new RemoveCompanyPageCoverCommand(id), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageCompanyPages)
        .WithTags(Tags.Admin);

        // Logo-ul se scoate imediat, fără aprobare: apare și pe cardurile din marketplace, nu doar
        // pe mini-site. Fără el se afișează inițialele firmei.
        app.MapDelete("admin/company-pages/{id:guid}/logo", async (
            Guid id,
            ICommandHandler<RemoveCompanyLogoCommand, AdminCompanyPageDetail> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminCompanyPageDetail> result =
                await handler.Handle(new RemoveCompanyLogoCommand(id), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageCompanyPages)
        .WithTags(Tags.Admin);
    }
}

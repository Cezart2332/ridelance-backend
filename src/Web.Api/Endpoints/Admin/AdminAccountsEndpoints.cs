using Application.Abstractions.Messaging;
using Application.Admin.Accounts;
using Application.Admin.Srl;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

/// <summary>Conturile clienților: lista firmelor și închiderea / redeschiderea unui cont.</summary>
internal sealed class AdminAccountsEndpoints : IEndpoint
{
    public sealed record CloseAccountRequest(string? Reason);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/srl-accounts", async (
            IQueryHandler<GetAdminSrlAccountsQuery, IReadOnlyList<AdminSrlAccountRow>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<AdminSrlAccountRow>> result = await handler.Handle(new GetAdminSrlAccountsQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);

        app.MapPost("admin/accounts/{userId:guid}/close", async (
            Guid userId,
            CloseAccountRequest request,
            ICommandHandler<CloseClientAccountCommand, ClientAccountStatusResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ClientAccountStatusResponse> result =
                await handler.Handle(new CloseClientAccountCommand(userId, request.Reason), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);

        app.MapPost("admin/accounts/{userId:guid}/reopen", async (
            Guid userId,
            ICommandHandler<ReopenClientAccountCommand, ClientAccountStatusResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<ClientAccountStatusResponse> result =
                await handler.Handle(new ReopenClientAccountCommand(userId), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);
    }
}

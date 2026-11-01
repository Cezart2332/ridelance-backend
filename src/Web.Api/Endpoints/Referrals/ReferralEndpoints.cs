using Application.Abstractions.Messaging;
using Application.Referrals;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Referrals;

/// <summary>Codul de recomandare al clientului, invitațiile lui și, pentru admin, ale oricărui client.</summary>
internal sealed class ReferralEndpoints : IEndpoint
{
    public sealed record InviteRequest(string Email);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder me = app.MapGroup("referrals").RequireAuthorization().WithTags(Tags.Users);

        me.MapGet("me", async (IQueryHandler<GetMyReferralsQuery, ReferralsResponse> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new GetMyReferralsQuery(), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        me.MapPost("invitations", async (InviteRequest request, ICommandHandler<InviteReferralCommand, ReferralsResponse> handler, CancellationToken cancellationToken) =>
            (await handler.Handle(new InviteReferralCommand(request.Email), cancellationToken)).Match(Results.Ok, CustomResults.Problem));

        app.MapGet("admin/clients/{userId:guid}/referrals", async (
            Guid userId,
            IQueryHandler<GetClientReferralsQuery, ReferralsResponse> handler,
            CancellationToken cancellationToken) =>
            (await handler.Handle(new GetClientReferralsQuery(userId), cancellationToken)).Match(Results.Ok, CustomResults.Problem))
            .RequireAuthorization()
            .WithTags(Tags.Users);
    }
}

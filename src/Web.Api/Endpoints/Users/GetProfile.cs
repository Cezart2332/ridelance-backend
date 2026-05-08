using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Users.GetProfile;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class GetProfile : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("users/profile", async (
            IUserContext userContext,
            IQueryHandler<GetProfileQuery, UserProfileResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetProfileQuery(userContext.UserId);

            Result<UserProfileResponse> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }
}

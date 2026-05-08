using Application.Abstractions.Messaging;
using Application.Users.Register;
using Domain.Users;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class Register : IEndpoint
{
    public sealed record Request(
        string Email,
        string FirstName,
        string LastName,
        string Password,
        string? PhoneNumber = null,
        string Role = "Client");

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/register", async (
            Request request,
            ICommandHandler<RegisterUserCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out UserRole role))
            {
                role = UserRole.Client;
            }

            var command = new RegisterUserCommand(
                request.Email,
                request.FirstName,
                request.LastName,
                request.Password,
                role,
                request.PhoneNumber);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Users);
    }
}

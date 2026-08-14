using Application.Abstractions.Messaging;
using Application.Users.Register;
using Domain.Users;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

internal sealed class Register : IEndpoint
{
    /// <summary>
    /// <c>FirstName</c>/<c>LastName</c> rămân în contract, dar opționale (RL-05): consumatorii
    /// existenți care încă le trimit nu se sparg.
    /// </summary>
    public sealed record Request(
        string Email,
        string Password,
        string? FirstName = null,
        string? LastName = null,
        string? PhoneNumber = null,
        string Role = "Client");

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/register", async (
            Request request,
            ICommandHandler<RegisterUserCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out UserRole role)
                || role is UserRole.Admin or UserRole.Contabil)
            {
                role = UserRole.Client;
            }

            var command = new RegisterUserCommand(
                request.Email,
                request.Password,
                request.FirstName,
                request.LastName,
                role,
                request.PhoneNumber);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .WithTags(Tags.Users);
    }
}

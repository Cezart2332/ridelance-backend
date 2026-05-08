using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Create;
using Domain.PfaRegistrations;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class Create : IEndpoint
{
    public sealed record Request(
        string RegistrationType,
        string? FullName,
        string? Phone,
        int? ContractDuration,
        string? Street,
        string? Number,
        string? City,
        string? County,
        bool IsOwner);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("pfa-registrations", async (
            Request request,
            IUserContext userContext,
            ICommandHandler<CreatePfaRegistrationCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<RegistrationType>(request.RegistrationType, ignoreCase: true, out RegistrationType regType))
            {
                regType = RegistrationType.NuAmPfa;
            }

            var command = new CreatePfaRegistrationCommand(
                userContext.UserId,
                regType,
                request.FullName,
                request.Phone,
                request.ContractDuration,
                request.Street,
                request.Number,
                request.City,
                request.County,
                request.IsOwner);

            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);
    }
}

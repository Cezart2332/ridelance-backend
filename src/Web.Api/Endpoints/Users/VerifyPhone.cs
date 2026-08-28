using Application.Abstractions.Messaging;
using Application.Users.PhoneVerification;
using SharedKernel;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Users;

/// <summary>
/// Confirmarea numărului de telefon.
/// </summary>
/// <remarks>
/// Spre deosebire de confirmarea emailului, care e anonimă fiindcă se face imediat după
/// înregistrare, aici e nevoie de sesiune: numărul se confirmă din setările contului, iar SMS-ul
/// e plătit — un endpoint anonim ar fi un buton prin care oricine ne cheltuie creditul.
/// </remarks>
internal sealed class SendPhoneCode : IEndpoint
{
    public sealed record Request(string? PhoneNumber);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/phone/send-code", async (
            Request? request,
            ICommandHandler<SendPhoneCodeCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new SendPhoneCodeCommand(request?.PhoneNumber), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }
}

internal sealed class ConfirmPhone : IEndpoint
{
    public sealed record Request(string Code);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("users/phone/confirm", async (
            Request request,
            ICommandHandler<ConfirmPhoneCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new ConfirmPhoneCommand(request.Code), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization()
        .WithTags(Tags.Users);
    }
}

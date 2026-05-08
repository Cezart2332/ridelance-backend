using Application.Abstractions.Messaging;
using Application.Cars.Commands.DeleteCarImage;
using Application.Cars.Commands.UploadCarImage;
using SharedKernel;
using Web.Api.Infrastructure;
using Infrastructure.Authorization;

namespace Web.Api.Endpoints.Cars;

internal sealed class UploadImage : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cars/{id:guid}/images", async (
            Guid id,
            IFormFile file,
            ICommandHandler<UploadCarImageCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            using Stream stream = file.OpenReadStream();

            var command = new UploadCarImageCommand(id, file.FileName, stream, file.ContentType);
            Result<Guid> result = await handler.Handle(command, cancellationToken);

            return result.IsFailure
                ? CustomResults.Problem(result)
                : Results.Ok(new { imageId = result.Value, url = $"/uploads/cars/{result.Value}" });
        })
        .RequireAuthorization(Permissions.ManageCars)
        .DisableAntiforgery()
        .WithTags(Tags.Cars);
    }
}

internal sealed class DeleteImage : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("cars/{id:guid}/images/{imageId:guid}", async (
            Guid id,
            Guid imageId,
            ICommandHandler<DeleteCarImageCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new DeleteCarImageCommand(id, imageId), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}

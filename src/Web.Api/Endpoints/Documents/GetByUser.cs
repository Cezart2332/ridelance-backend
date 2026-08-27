using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Documents.GetByUser;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Documents;

internal sealed class GetByUser : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("documents", async (
            Guid? userId,
            IUserContext userContext,
            IQueryHandler<GetDocumentsByUserQuery, List<DocumentSummary>> handler,
            CancellationToken cancellationToken) =>
        {
            // If userId is provided and different from current user, requires admin/contabil role
            Guid targetUserId = userId ?? userContext.UserId;

            var query = new GetDocumentsByUserQuery(targetUserId);

            Result<List<DocumentSummary>> result = await handler.Handle(query, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Documents);
    }
}

internal sealed class GetCarDossier : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("documents/car/{carId:guid}", async (
            Guid carId,
            IQueryHandler<Application.Documents.CarDossier.GetCarDossierQuery,
                Application.Documents.CarDossier.CarDossierDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<Application.Documents.CarDossier.CarDossierDto> result = await handler.Handle(
                new Application.Documents.CarDossier.GetCarDossierQuery(carId),
                cancellationToken);

            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization()
        .WithTags(Tags.Documents);
    }
}

using Application.Abstractions.Messaging;
using Application.Taxes;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Taxes;

internal sealed class TaxObligationsEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("tax-obligations")
            .RequireAuthorization()
            .WithTags("TaxObligations");

        // Fără `pfaRegistrationId` întoarce obligațiile utilizatorului curent; cu el, ale
        // clientului indicat — aceeași rută servește și clientul, și contabila.
        group.MapGet(string.Empty, async (
            Guid? pfaRegistrationId,
            int? year,
            IQueryHandler<GetTaxObligationsQuery, List<TaxObligationResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<TaxObligationResponse>> result = await handler.Handle(
                new GetTaxObligationsQuery(pfaRegistrationId, year),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost(string.Empty, async (
            UpsertTaxObligationRequest request,
            ICommandHandler<UpsertTaxObligationCommand, TaxObligationResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<TaxObligationResponse> result = await handler.Handle(
                new UpsertTaxObligationCommand(
                    request.Id,
                    request.PfaRegistrationId,
                    request.Type,
                    request.PeriodYear,
                    request.PeriodMonth,
                    request.AmountDue,
                    request.DueDate,
                    request.Status,
                    request.DocumentId,
                    request.Note),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }

    internal sealed record UpsertTaxObligationRequest(
        Guid? Id,
        Guid PfaRegistrationId,
        string Type,
        int PeriodYear,
        int PeriodMonth,
        decimal AmountDue,
        DateOnly DueDate,
        string Status,
        Guid? DocumentId,
        string? Note);
}

using Application.Abstractions.Messaging;
using Application.Admin.TaxParameters;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

/// <summary>Plafoanele și cotele fiscale pe ani, editabile din „Privire de ansamblu".</summary>
internal sealed class AdminTaxParametersEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/tax-parameters", async (
            int? year,
            IQueryHandler<GetTaxParametersQuery, TaxParametersResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<TaxParametersResponse> result = await handler.Handle(new GetTaxParametersQuery(year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageTaxParameters)
        .WithTags(Tags.Admin);

        app.MapPut("admin/tax-parameters/{year:int}", async (
            int year,
            TaxParametersValues values,
            ICommandHandler<UpdateTaxParametersCommand, TaxParametersResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<TaxParametersResponse> result = await handler.Handle(new UpdateTaxParametersCommand(year, values), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageTaxParameters)
        .WithTags(Tags.Admin);

        app.MapDelete("admin/tax-parameters/{year:int}", async (
            int year,
            ICommandHandler<ResetTaxParametersCommand, TaxParametersResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<TaxParametersResponse> result = await handler.Handle(new ResetTaxParametersCommand(year), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageTaxParameters)
        .WithTags(Tags.Admin);
    }
}

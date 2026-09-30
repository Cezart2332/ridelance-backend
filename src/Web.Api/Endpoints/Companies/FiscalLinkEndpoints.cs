using Application.Abstractions.Messaging;
using Application.FiscalLink;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Companies;

/// <summary>Conexiuni → FiscalLink: casa de marcat a PFA-ului.</summary>
internal sealed class FiscalLinkEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("connections/fiscallink")
            .RequireAuthorization()
            .WithTags(Tags.Companies);

        group.MapGet(string.Empty, async (
            IQueryHandler<GetFiscalLinkConnectionQuery, FiscalLinkConnectionDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<FiscalLinkConnectionDto> result = await handler.Handle(new GetFiscalLinkConnectionQuery(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapPost(string.Empty, async (
            ICommandHandler<ConnectFiscalLinkCommand, FiscalLinkConnectionDto> handler,
            CancellationToken cancellationToken) =>
        {
            Result<FiscalLinkConnectionDto> result = await handler.Handle(new ConnectFiscalLinkCommand(), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }
}

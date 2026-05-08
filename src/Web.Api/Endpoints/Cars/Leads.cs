using Application.Abstractions.Messaging;
using Application.Cars.Commands.SubmitCarLead;
using Application.Cars.Commands.UpdateLeadStatus;
using Application.Cars.Queries.GetLeadsAdmin;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Infrastructure;
using Infrastructure.Authorization;

namespace Web.Api.Endpoints.Cars;

internal sealed class SubmitLead : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("cars/{id:guid}/leads", async (
            Guid id,
            [FromBody] SubmitLeadRequest request,
            ICommandHandler<SubmitCarLeadCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new SubmitCarLeadCommand(
                id, request.UserName, request.UserEmail, request.UserPhone,
                request.InterestType);

            Result<Guid> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(new { leadId = result.Value });
        })
        .WithTags(Tags.Cars);
    }
}

internal sealed record SubmitLeadRequest(
    string UserName, string UserEmail, string UserPhone,
    string InterestType);

internal sealed class GetLeads : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("cars/leads", async (
            Guid? carId,
            string? status,
            IQueryHandler<GetLeadsAdminQuery, List<CarLeadDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<CarLeadDto>> result = await handler.Handle(new GetLeadsAdminQuery(carId, status), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}

internal sealed class UpdateLeadStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("cars/leads/{leadId:guid}/status", async (
            Guid leadId,
            [FromBody] UpdateLeadStatusRequest request,
            ICommandHandler<UpdateLeadStatusCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new UpdateLeadStatusCommand(leadId, request.Status, request.AdminNote), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .RequireAuthorization(Permissions.ManageCars)
        .WithTags(Tags.Cars);
    }
}

internal sealed record UpdateLeadStatusRequest(string Status, string? AdminNote);

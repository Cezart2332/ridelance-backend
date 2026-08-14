using Application.Abstractions.Messaging;
using Application.Cars.Commands.SubmitCarLead;
using Application.Cars.Commands.UpdateLeadStatus;
using Application.Cars.Queries.GetLeadsAdmin;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Infrastructure;

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
                request.City, request.InterestType, request.ConsentAccepted,
                request.Intent, request.PreferredStartDate, request.Weeks,
                request.HasPlatformAccount, request.Message);

            Result<Guid> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(new { leadId = result.Value });
        })
        .WithTags(Tags.Cars);
    }
}

internal sealed record SubmitLeadRequest(
    string UserName, string UserEmail, string UserPhone,
    string City, string InterestType, bool ConsentAccepted,
    string? Intent, DateOnly? PreferredStartDate, int? Weeks,
    bool? HasPlatformAccount, string? Message);

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
        // Role-based filtering happens in the handler: admins see everything,
        // posters only the leads for their own listings.
        .RequireAuthorization()
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
        // Ownership is enforced in the handler (admin or the listing's poster).
        .RequireAuthorization()
        .WithTags(Tags.Cars);
    }
}

internal sealed record UpdateLeadStatusRequest(string Status, string? AdminNote);

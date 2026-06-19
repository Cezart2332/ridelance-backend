using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.InternalNotes;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaRegistrations;

internal sealed class InternalNotesEndpoints : IEndpoint
{
    public sealed record CreateNoteRequest(int Year, int Month, string Content);
    public sealed record UpdateNoteRequest(string Content);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("pfa-registrations/{id:guid}/internal-notes", async (
            Guid id,
            int? year,
            int? month,
            IQueryHandler<GetPfaInternalNotesQuery, IReadOnlyList<PfaInternalNoteResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetPfaInternalNotesQuery(id, year, month);
            Result<IReadOnlyList<PfaInternalNoteResponse>> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewAssignedClients)
        .WithTags(Tags.PfaRegistrations);

        app.MapPost("pfa-registrations/{id:guid}/internal-notes", async (
            Guid id,
            CreateNoteRequest request,
            ICommandHandler<CreatePfaInternalNoteCommand, PfaInternalNoteResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreatePfaInternalNoteCommand(id, request.Year, request.Month, request.Content);
            Result<PfaInternalNoteResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageClientIncome)
        .WithTags(Tags.PfaRegistrations);

        app.MapPut("pfa-registrations/internal-notes/{noteId:guid}", async (
            Guid noteId,
            UpdateNoteRequest request,
            ICommandHandler<UpdatePfaInternalNoteCommand, PfaInternalNoteResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new UpdatePfaInternalNoteCommand(noteId, request.Content);
            Result<PfaInternalNoteResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageClientIncome)
        .WithTags(Tags.PfaRegistrations);

        app.MapDelete("pfa-registrations/internal-notes/{noteId:guid}", async (
            Guid noteId,
            ICommandHandler<DeletePfaInternalNoteCommand> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new DeletePfaInternalNoteCommand(noteId);
            Result result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.NoContent, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageClientIncome)
        .WithTags(Tags.PfaRegistrations);

        app.MapGet("pfa-registrations/{id:guid}/activity-logs", async (
            Guid id,
            IQueryHandler<GetPfaActivityLogsQuery, IReadOnlyList<PfaActivityLogResponse>> handler,
            CancellationToken cancellationToken) =>
        {
            var query = new GetPfaActivityLogsQuery(id);
            Result<IReadOnlyList<PfaActivityLogResponse>> result = await handler.Handle(query, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.PfaRegistrations);
    }
}

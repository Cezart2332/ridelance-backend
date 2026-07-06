using Application.Abstractions.Messaging;
using Application.Office;
using Application.Office.CancelOfficeAppointment;
using Application.Office.CreateOfficeAppointment;
using Application.Office.GetOfficeAppointments;
using Application.Office.GetOfficeAvailability;
using Application.Office.GetOfficeDaySlots;
using System.Security.Claims;
using Application.Office.OfficeBlocks;
using Application.Office.OfficeSchedule;
using Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Office;

/// <summary>
/// Office visit calendar. Availability and booking are public (used on the
/// landing page); management endpoints require the office:manage permission.
/// </summary>
internal sealed class OfficeCalendarEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // ── Public: month availability ──
        app.MapGet("office/availability", async (
            int year,
            int month,
            IQueryHandler<GetOfficeAvailabilityQuery, OfficeMonthAvailabilityResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<OfficeMonthAvailabilityResponse> result =
                await handler.Handle(new GetOfficeAvailabilityQuery(year, month), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .WithTags(Tags.Office);

        // ── Public: free slots for one day ──
        app.MapGet("office/slots", async (
            string date,
            IQueryHandler<GetOfficeDaySlotsQuery, OfficeDaySlotsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!OfficeCalendar.TryParseDate(date, out DateOnly parsed))
            {
                return Results.Problem(statusCode: 400, detail: "Data este invalidă.");
            }

            Result<OfficeDaySlotsResponse> result =
                await handler.Handle(new GetOfficeDaySlotsQuery(parsed), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .WithTags(Tags.Office);

        // ── Public: book a visit (also used, authenticated, from the PFA dashboard) ──
        app.MapPost("office/appointments", async (
            [FromBody] BookOfficeVisitRequest request,
            HttpContext httpContext,
            ICommandHandler<CreateOfficeAppointmentCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            // Anonymous on the landing page; carries the user id when booked from the app.
            Guid? userId = Guid.TryParse(
                httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier),
                out Guid parsedUserId)
                ? parsedUserId
                : null;

            var command = new CreateOfficeAppointmentCommand(
                request.Date,
                request.Time,
                request.FullName,
                request.Email,
                request.Phone,
                request.Reason ?? string.Empty,
                userId);

            Result<Guid> result = await handler.Handle(command, cancellationToken);
            return result.IsFailure
                ? CustomResults.Problem(result)
                : Results.Ok(new { appointmentId = result.Value });
        })
        .WithTags(Tags.Office);

        // ── Admin: appointments list ──
        app.MapGet("office/admin/appointments", async (
            string? from,
            string? to,
            IQueryHandler<GetOfficeAppointmentsQuery, List<OfficeAppointmentDto>> handler,
            CancellationToken cancellationToken) =>
        {
            DateOnly? fromDate = OfficeCalendar.TryParseDate(from, out DateOnly f) ? f : null;
            DateOnly? toDate = OfficeCalendar.TryParseDate(to, out DateOnly t) ? t : null;

            Result<List<OfficeAppointmentDto>> result =
                await handler.Handle(new GetOfficeAppointmentsQuery(fromDate, toDate), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .HasPermission(Permissions.ManageOfficeCalendar)
        .WithTags(Tags.Office);

        // ── Admin: day detail (slots with visitor + block info) ──
        app.MapGet("office/admin/day", async (
            string date,
            IQueryHandler<GetOfficeDaySlotsQuery, OfficeDaySlotsResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!OfficeCalendar.TryParseDate(date, out DateOnly parsed))
            {
                return Results.Problem(statusCode: 400, detail: "Data este invalidă.");
            }

            Result<OfficeDaySlotsResponse> result =
                await handler.Handle(new GetOfficeDaySlotsQuery(parsed, IncludeDetails: true), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .HasPermission(Permissions.ManageOfficeCalendar)
        .WithTags(Tags.Office);

        // ── Admin: cancel an appointment ──
        app.MapDelete("office/admin/appointments/{id:guid}", async (
            Guid id,
            ICommandHandler<CancelOfficeAppointmentCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new CancelOfficeAppointmentCommand(id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .HasPermission(Permissions.ManageOfficeCalendar)
        .WithTags(Tags.Office);

        // ── Admin: weekly schedule ──
        app.MapGet("office/admin/schedule", async (
            IQueryHandler<GetOfficeScheduleQuery, List<OfficeScheduleDayDto>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<List<OfficeScheduleDayDto>> result =
                await handler.Handle(new GetOfficeScheduleQuery(), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.Ok(result.Value);
        })
        .HasPermission(Permissions.ManageOfficeCalendar)
        .WithTags(Tags.Office);

        app.MapPut("office/admin/schedule", async (
            [FromBody] List<OfficeScheduleDayDto> days,
            ICommandHandler<UpsertOfficeScheduleCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new UpsertOfficeScheduleCommand(days), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .HasPermission(Permissions.ManageOfficeCalendar)
        .WithTags(Tags.Office);

        // ── Admin: block / unblock slots or whole days ──
        app.MapPost("office/admin/blocks", async (
            [FromBody] BlockSlotRequest request,
            ICommandHandler<BlockOfficeSlotCommand, Guid> handler,
            CancellationToken cancellationToken) =>
        {
            Result<Guid> result = await handler.Handle(
                new BlockOfficeSlotCommand(request.Date, request.Time, request.Note),
                cancellationToken);
            return result.IsFailure
                ? CustomResults.Problem(result)
                : Results.Ok(new { blockId = result.Value });
        })
        .HasPermission(Permissions.ManageOfficeCalendar)
        .WithTags(Tags.Office);

        app.MapDelete("office/admin/blocks/{id:guid}", async (
            Guid id,
            ICommandHandler<UnblockOfficeSlotCommand> handler,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.Handle(new UnblockOfficeSlotCommand(id), cancellationToken);
            return result.IsFailure ? CustomResults.Problem(result) : Results.NoContent();
        })
        .HasPermission(Permissions.ManageOfficeCalendar)
        .WithTags(Tags.Office);
    }
}

internal sealed record BookOfficeVisitRequest(
    string Date,
    string Time,
    string FullName,
    string Email,
    string Phone,
    string? Reason);

internal sealed record BlockSlotRequest(string Date, string? Time, string? Note);

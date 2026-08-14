using Application.Abstractions.Messaging;
using Application.Admin;
using Domain.Payments;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

internal sealed class AdminOverviewEndpoints : IEndpoint
{
    public sealed record ChangePlanRequest(string Plan, string Effective);
    public sealed record ApplyDiscountRequest(string Type, decimal Value, string? Note);
    public sealed record AdminNoteRequest(string? Note);
    public sealed record SuspendRequest(string? Reason);
    public sealed record UpdateInternalNoteRequest(string Content);
    public sealed record UpdateClientNameRequest(string? FirstName, string? LastName);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/overview", async (
            string? periodPreset,
            DateTime? dateFrom,
            DateTime? dateTo,
            string? revenueType,
            string? product,
            string? paymentStatus,
            string? plan,
            string? city,
            string? partner,
            IQueryHandler<GetAdminOverviewQuery, AdminOverviewResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var filters = new AdminOverviewFilters(
                periodPreset ?? "current_month",
                dateFrom,
                dateTo,
                revenueType,
                product,
                paymentStatus,
                plan,
                city,
                partner);

            Result<AdminOverviewResponse> result = await handler.Handle(new GetAdminOverviewQuery(filters), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ViewServiceOrders)
        .WithTags(Tags.Admin);

        app.MapGet("admin/pfas/{id:guid}/details", async (
            Guid id,
            IQueryHandler<GetAdminPfaDetailQuery, AdminPfaDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminPfaDetailResponse> result = await handler.Handle(new GetAdminPfaDetailQuery(id), cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);

        app.MapPost("admin/pfas/{id:guid}/change-plan", async (
            Guid id,
            ChangePlanRequest request,
            ICommandHandler<ChangeAdminPfaPlanCommand, AdminPfaDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.Plan, ignoreCase: true, out SubscriptionPlan plan))
            {
                return CustomResults.Problem(Result.Failure(
                    Error.Problem("Plan.Invalid", "Planul ales nu este valid.")));
            }

            var command = new ChangeAdminPfaPlanCommand(id, plan, request.Effective);
            Result<AdminPfaDetailResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);

        app.MapPost("admin/pfas/{id:guid}/discount", async (
            Guid id,
            ApplyDiscountRequest request,
            ICommandHandler<ApplyAdminPfaDiscountCommand, AdminPfaDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new ApplyAdminPfaDiscountCommand(id, request.Type, request.Value, request.Note);
            Result<AdminPfaDetailResponse> result = await handler.Handle(command, cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);

        app.MapPost("admin/pfas/{id:guid}/suspend", async (
            Guid id,
            SuspendRequest request,
            ICommandHandler<SuspendAdminPfaCommand, AdminPfaDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminPfaDetailResponse> result = await handler.Handle(
                new SuspendAdminPfaCommand(id, request.Reason),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);

        app.MapPost("admin/pfas/{id:guid}/reactivate", async (
            Guid id,
            AdminNoteRequest request,
            ICommandHandler<ReactivateAdminPfaCommand, AdminPfaDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminPfaDetailResponse> result = await handler.Handle(
                new ReactivateAdminPfaCommand(id, request.Note),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);

        app.MapPut("admin/pfas/{id:guid}/internal-note", async (
            Guid id,
            UpdateInternalNoteRequest request,
            ICommandHandler<UpdateAdminPfaInternalNoteCommand, AdminPfaDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminPfaDetailResponse> result = await handler.Handle(
                new UpdateAdminPfaInternalNoteCommand(id, request.Content),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);

        // RL-05 — completarea manuală a numelui, pentru conturile la care OCR-ul nu a ajuns încă.
        app.MapPut("admin/pfas/{id:guid}/client-name", async (
            Guid id,
            UpdateClientNameRequest request,
            ICommandHandler<UpdateAdminPfaClientNameCommand, AdminPfaDetailResponse> handler,
            CancellationToken cancellationToken) =>
        {
            Result<AdminPfaDetailResponse> result = await handler.Handle(
                new UpdateAdminPfaClientNameCommand(id, request.FirstName, request.LastName),
                cancellationToken);
            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManagePfaRegistrations)
        .WithTags(Tags.Admin);
    }
}

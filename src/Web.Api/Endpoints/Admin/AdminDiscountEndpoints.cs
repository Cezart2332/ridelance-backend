using Application.Abstractions.Messaging;
using Application.Admin.Discounts;
using Domain.Payments;
using Infrastructure.Authorization;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Admin;

internal sealed class AdminDiscountEndpoints : IEndpoint
{
    public sealed record CreateRequest(
        string Code,
        long? AmountOffBani,
        decimal? PercentOff,
        long? MaxRedemptions,
        bool AppliesToAllPayments = false,
        DateTime? ExpiresAtUtc = null);

    public sealed record SetActiveRequest(bool Active);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("admin/discounts", async (
            IQueryHandler<GetDiscountCodesQuery, IReadOnlyList<DiscountCode>> handler,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<DiscountCode>> result =
                await handler.Handle(new GetDiscountCodesQuery(), cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageDiscounts)
        .WithTags(Tags.Admin);

        app.MapPost("admin/discounts", async (
            CreateRequest request,
            ICommandHandler<CreateDiscountCodeCommand, DiscountCode> handler,
            CancellationToken cancellationToken) =>
        {
            var command = new CreateDiscountCodeCommand(
                request.Code,
                request.AmountOffBani,
                request.PercentOff,
                request.MaxRedemptions,
                request.AppliesToAllPayments,
                request.ExpiresAtUtc);

            Result<DiscountCode> result = await handler.Handle(command, cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageDiscounts)
        .WithTags(Tags.Admin);

        app.MapPost("admin/discounts/{promotionCodeId}/active", async (
            string promotionCodeId,
            SetActiveRequest request,
            ICommandHandler<SetDiscountCodeActiveCommand, DiscountCode> handler,
            CancellationToken cancellationToken) =>
        {
            Result<DiscountCode> result = await handler.Handle(
                new SetDiscountCodeActiveCommand(promotionCodeId, request.Active),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        })
        .RequireAuthorization()
        .HasPermission(Permissions.ManageDiscounts)
        .WithTags(Tags.Admin);
    }
}

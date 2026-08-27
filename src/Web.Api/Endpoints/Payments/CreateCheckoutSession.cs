using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Payments.CreateCheckoutSession;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.Payments;

internal sealed class CreateCheckoutSession : IEndpoint
{
    public sealed record Request(
        string Mode,          // "payment" or "subscription"
        string Plan,          // e.g. "solo", "start", "pro", "infiintare_pfa"
        // "Monthly" | "Annual". Absent sau nerecunoscut înseamnă lunar.
        string? Cycle,
        string? SuccessUrl = null,
        string? CancelUrl = null,
        bool IsPlanChange = false,
        bool BcrDiscountRequested = false);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("payments/checkout-session", async (
            Request request,
            IUserContext userContext,
            IApplicationDbContext dbContext,
            ICommandHandler<CreateCheckoutSessionCommand, string> handler,
            CancellationToken cancellationToken) =>
        {
            // Load user email
            string? email = await dbContext.Users
                .Where(u => u.Id == userContext.UserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(cancellationToken);

            var command = new CreateCheckoutSessionCommand(
                userContext.UserId,
                email ?? string.Empty,
                request.Mode,
                request.Plan,
                ParseCycle(request.Cycle),
                request.SuccessUrl,
                request.CancelUrl,
                request.IsPlanChange,
                request.BcrDiscountRequested);

            Result<string> result = await handler.Handle(command, cancellationToken);

            return result.Match(
                clientSecret => Results.Ok(new { clientSecret }),
                CustomResults.Problem);
        })
        .RequireAuthorization()
        .WithTags(Tags.Payments);
    }

    /// <summary>
    /// Lunar e implicit: un ciclu lipsă sau scris greșit nu are voie să vândă din greșeală un an
    /// întreg. Greșeala în direcția cealaltă costă clientul, nu doar o reîncercare.
    /// </summary>
    private static SubscriptionBillingCycle ParseCycle(string? cycle) =>
        Enum.TryParse(cycle, ignoreCase: true, out SubscriptionBillingCycle parsed)
            ? parsed
            : SubscriptionBillingCycle.Monthly;
}

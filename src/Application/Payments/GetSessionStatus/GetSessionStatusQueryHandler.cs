using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using SharedKernel;

namespace Application.Payments.GetSessionStatus;

internal sealed class GetSessionStatusQueryHandler(IStripeService stripeService)
    : IQueryHandler<GetSessionStatusQuery, SessionStatusResponse>
{
    public async Task<Result<SessionStatusResponse>> Handle(
        GetSessionStatusQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.SessionId))
        {
            return Result.Failure<SessionStatusResponse>(Error.Problem("Stripe.SessionIdRequired", "Session ID is required."));
        }

        try
        {
            (string status, string? email) = await stripeService.GetSessionStatusAsync(query.SessionId, cancellationToken);
            return new SessionStatusResponse(status, email);
        }
        catch (Exception ex)
        {
            return Result.Failure<SessionStatusResponse>(Error.Problem("Stripe.SessionStatusFailed", ex.Message));
        }
    }
}

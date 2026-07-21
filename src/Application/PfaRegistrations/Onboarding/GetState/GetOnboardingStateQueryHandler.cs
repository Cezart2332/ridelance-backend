using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Payments;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.GetState;

internal sealed class GetOnboardingStateQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetOnboardingStateQuery, OnboardingStateResponse>
{
    public async Task<Result<OnboardingStateResponse>> Handle(
        GetOnboardingStateQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .AsNoTracking()
            .Include(r => r.OnboardingSections)
            .Where(r => r.UserId == query.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        bool hasPaidInfiintare = await InfiintarePaymentCheck.HasPaidAsync(
            context, query.UserId, cancellationToken);

        return Result.Success(OnboardingStateBuilder.Build(registration, hasPaidInfiintare));
    }
}

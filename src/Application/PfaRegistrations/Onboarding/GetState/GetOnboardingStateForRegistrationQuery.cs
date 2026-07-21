using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Payments;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.GetState;

public sealed record GetOnboardingStateForRegistrationQuery(Guid RegistrationId) : IQuery<OnboardingStateResponse>;

internal sealed class GetOnboardingStateForRegistrationQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetOnboardingStateForRegistrationQuery, OnboardingStateResponse>
{
    public async Task<Result<OnboardingStateResponse>> Handle(
        GetOnboardingStateForRegistrationQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .AsNoTracking()
            .Include(r => r.OnboardingSections)
            .SingleOrDefaultAsync(r => r.Id == query.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Result.Failure<OnboardingStateResponse>(
                PfaRegistrationErrors.NotFound(query.RegistrationId));
        }

        bool hasPaidInfiintare = await InfiintarePaymentCheck.HasPaidAsync(
            context, registration.UserId, cancellationToken);

        return Result.Success(OnboardingStateBuilder.Build(registration, hasPaidInfiintare));
    }
}

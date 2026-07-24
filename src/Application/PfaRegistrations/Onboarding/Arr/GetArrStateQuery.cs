using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

public sealed record GetArrStateQuery(Guid UserId) : IQuery<ArrStateResponse?>;

internal sealed class GetArrStateQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetArrStateQuery, ArrStateResponse?>
{
    public async Task<Result<ArrStateResponse?>> Handle(GetArrStateQuery query, CancellationToken cancellationToken)
    {
        ArrAuthorizationRequest? request = await context.ArrAuthorizationRequests
            .AsNoTracking()
            .Where(a => a.PfaRegistration.UserId == query.UserId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return Result.Success(request is null ? null : ArrShared.ToResponse(request));
    }
}

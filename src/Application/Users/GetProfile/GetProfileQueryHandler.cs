using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Users.AccountVerification;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.GetProfile;

internal sealed class GetProfileQueryHandler(IApplicationDbContext context, AccountVerificationGate verificationGate)
    : IQueryHandler<GetProfileQuery, UserProfileResponse>
{
    public async Task<Result<UserProfileResponse>> Handle(
        GetProfileQuery query,
        CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == query.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserProfileResponse>(UserErrors.NotFound(query.UserId));
        }

        PendingVerification pending = verificationGate.Pending(user);

        return new UserProfileResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.IsPhoneVerified,
            user.Role.ToString(),
            user.CreatedAtUtc,
            user.IsEmailVerified,
            pending.Email,
            pending.Phone);
    }
}

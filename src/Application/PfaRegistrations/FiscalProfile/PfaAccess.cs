using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.FiscalProfile;

internal static class PfaAccess
{
    public static async Task<Result<PfaRegistration>> EnsureCanViewAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Guid pfaRegistrationId,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .Include(p => p.User)
            .SingleOrDefaultAsync(p => p.Id == pfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<PfaRegistration>(
                Error.NotFound("PfaRegistration.NotFound", "PFA registration was not found."));
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<PfaRegistration>(
                Error.Failure("Users.NotFound", "Current user was not found."));
        }

        bool allowed = caller.Role == UserRole.Admin
            || pfa.UserId == userContext.UserId
            || caller.Role == UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId;

        if (!allowed)
        {
            return Result.Failure<PfaRegistration>(
                Error.Failure("PfaRegistration.Forbidden", "You do not have access to this PFA registration."));
        }

        return pfa;
    }

    public static async Task<Result<PfaRegistration>> EnsureCanManageAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Guid pfaRegistrationId,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> result = await EnsureCanViewAsync(context, userContext, pfaRegistrationId, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        bool canManage = caller?.Role == UserRole.Admin
            || (caller?.Role == UserRole.Contabil && result.Value.AssignedContabilId == userContext.UserId);

        if (!canManage)
        {
            return Result.Failure<PfaRegistration>(
                Error.Failure("PfaRegistration.Forbidden", "Only admins or the assigned accountant can update fiscal profile settings."));
        }

        return result;
    }
}

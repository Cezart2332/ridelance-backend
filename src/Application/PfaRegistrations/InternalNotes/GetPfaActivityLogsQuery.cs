using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.InternalNotes;

public sealed record GetPfaActivityLogsQuery(
    Guid PfaRegistrationId) : IQuery<IReadOnlyList<PfaActivityLogResponse>>;

internal sealed class GetPfaActivityLogsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetPfaActivityLogsQuery, IReadOnlyList<PfaActivityLogResponse>>
{
    public async Task<Result<IReadOnlyList<PfaActivityLogResponse>>> Handle(
        GetPfaActivityLogsQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == query.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<IReadOnlyList<PfaActivityLogResponse>>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<IReadOnlyList<PfaActivityLogResponse>>(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        bool hasAccess = caller.Role is UserRole.Admin
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId
            || caller.Role is UserRole.Client && pfa.UserId == userContext.UserId;

        if (!hasAccess)
        {
            return Result.Failure<IReadOnlyList<PfaActivityLogResponse>>(
                Error.Failure("Pfa.AccessDenied", "Nu ai acces la istoricul acestui client."));
        }

        List<PfaActivityLogResponse> logs = await context.PfaActivityLogs
            .Include(l => l.PerformedByUser)
            .Where(l => l.PfaRegistrationId == query.PfaRegistrationId)
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => new PfaActivityLogResponse(
                l.Id,
                l.PfaRegistrationId,
                l.ActivityType,
                l.Description,
                l.CreatedAtUtc,
                l.PerformedByUserId,
                $"{l.PerformedByUser.FirstName} {l.PerformedByUser.LastName}"))
            .ToListAsync(cancellationToken);

        return logs;
    }
}

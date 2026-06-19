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

public sealed record GetPfaInternalNotesQuery(
    Guid PfaRegistrationId,
    int? Year,
    int? Month) : IQuery<IReadOnlyList<PfaInternalNoteResponse>>;

internal sealed class GetPfaInternalNotesQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetPfaInternalNotesQuery, IReadOnlyList<PfaInternalNoteResponse>>
{
    public async Task<Result<IReadOnlyList<PfaInternalNoteResponse>>> Handle(
        GetPfaInternalNotesQuery query,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == query.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<IReadOnlyList<PfaInternalNoteResponse>>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<IReadOnlyList<PfaInternalNoteResponse>>(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        if (caller.Role is UserRole.Client)
        {
            return Result.Failure<IReadOnlyList<PfaInternalNoteResponse>>(
                Error.Failure("Pfa.AccessDenied", "Nu ai acces la notele interne."));
        }

        bool hasAccess = caller.Role is UserRole.Admin
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId;

        if (!hasAccess)
        {
            return Result.Failure<IReadOnlyList<PfaInternalNoteResponse>>(
                Error.Failure("Pfa.AccessDenied", "Nu ai acces la aceste date."));
        }

        IQueryable<PfaInternalNote> queryable = context.PfaInternalNotes
            .Include(n => n.CreatedByUser)
            .Where(n => n.PfaRegistrationId == query.PfaRegistrationId);

        if (query.Year.HasValue)
        {
            queryable = queryable.Where(n => n.Year == query.Year.Value);
        }

        if (query.Month.HasValue)
        {
            queryable = queryable.Where(n => n.Month == query.Month.Value);
        }

        List<PfaInternalNoteResponse> notes = await queryable
            .AsNoTracking()
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new PfaInternalNoteResponse(
                n.Id,
                n.PfaRegistrationId,
                n.Year,
                n.Month,
                n.Content,
                n.CreatedByUserId,
                $"{n.CreatedByUser.FirstName} {n.CreatedByUser.LastName}",
                n.CreatedAtUtc,
                n.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return notes;
    }
}

using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.GetAll;

internal sealed class GetAllPfaRegistrationsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetAllPfaRegistrationsQuery, PfaRegistrationListResponse>
{
    public async Task<Result<PfaRegistrationListResponse>> Handle(
        GetAllPfaRegistrationsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<PfaRegistration> queryable = context.PfaRegistrations.AsQueryable();

        // If the caller is a Contabil, only show their assigned PFAs
        User? caller = await context.Users
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller?.Role == UserRole.Contabil)
        {
            queryable = queryable.Where(r => r.AssignedContabilId == userContext.UserId);
        }

        int totalCount = await queryable.CountAsync(cancellationToken);

        var pagedData = await queryable
            .AsNoTracking()
            .Select(r => new
            {
                r.Id,
                r.UserId,
                UserEmail = r.User.Email,
                UserFirstName = r.User.FirstName,
                UserLastName = r.User.LastName,
                r.RegistrationType,
                r.Status,
                DocumentCount = r.Documents.Count,
                r.CreatedAtUtc,
                LastActivityAtUtc = context.ChatRooms
                    .Where(cr => cr.ClientUserId == r.UserId)
                    .OrderByDescending(cr => cr.LastMessageAtUtc)
                    .Select(cr => (DateTime?)cr.LastMessageAtUtc)
                    .FirstOrDefault()
            })
            .OrderByDescending(x => x.LastActivityAtUtc ?? x.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = pagedData
            .Select(x => new PfaRegistrationSummary(
                x.Id,
                x.UserId,
                x.UserEmail,
                $"{x.UserFirstName} {x.UserLastName}",
                x.RegistrationType.ToString(),
                x.Status.ToString(),
                x.DocumentCount,
                x.CreatedAtUtc,
                x.LastActivityAtUtc))
            .ToList();

        return new PfaRegistrationListResponse(items, totalCount);
    }
}

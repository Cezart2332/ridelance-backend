using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Queries.GetLeadsAdmin;

public sealed record GetLeadsAdminQuery(Guid? CarId = null, string? Status = null) : IQuery<List<CarLeadDto>>;

public sealed record CarLeadDto(
    Guid Id,
    Guid CarId,
    string CarName,
    string UserName,
    string UserEmail,
    string UserPhone,
    string City,
    string InterestType,
    string Status,
    string? AdminNote,
    DateTime CreatedAtUtc);

internal sealed class GetLeadsAdminQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetLeadsAdminQuery, List<CarLeadDto>>
{
    public async Task<Result<List<CarLeadDto>>> Handle(GetLeadsAdminQuery query, CancellationToken cancellationToken)
    {
        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null || (caller.Role != UserRole.Admin && caller.Role != UserRole.CarPoster))
        {
            return Result.Failure<List<CarLeadDto>>(
                Error.Failure("CarLead.Forbidden", "Nu ai acces la lead-urile anunțurilor."));
        }

        IQueryable<CarLead> queryable = context.CarLeads.AsNoTracking();

        // Posters only see leads for their own listings.
        if (caller.Role == UserRole.CarPoster)
        {
            queryable = queryable.Where(l =>
                context.Cars.Any(c => c.Id == l.CarId && c.PostedByUserId == userContext.UserId));
        }

        if (query.CarId.HasValue)
        {
            queryable = queryable.Where(l => l.CarId == query.CarId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<CarLeadStatus>(query.Status, out CarLeadStatus status))
        {
            queryable = queryable.Where(l => l.Status == status);
        }

        List<CarLeadDto> leads = await queryable
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => new CarLeadDto(
                l.Id, l.CarId, l.CarName,
                l.UserName, l.UserEmail, l.UserPhone,
                l.City,
                l.InterestType,
                l.Status.ToString(), l.AdminNote,
                l.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return leads;
    }
}

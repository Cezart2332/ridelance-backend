using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
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
    string InterestType,
    string Status,
    string? AdminNote,
    DateTime CreatedAtUtc);

internal sealed class GetLeadsAdminQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetLeadsAdminQuery, List<CarLeadDto>>
{
    public async Task<Result<List<CarLeadDto>>> Handle(GetLeadsAdminQuery query, CancellationToken cancellationToken)
    {
        IQueryable<CarLead> queryable = context.CarLeads.AsNoTracking();

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
                l.InterestType,
                l.Status.ToString(), l.AdminNote,
                l.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return leads;
    }
}

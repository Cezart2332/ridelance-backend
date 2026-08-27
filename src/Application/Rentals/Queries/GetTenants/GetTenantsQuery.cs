using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Rentals.Queries.GetRentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Queries.GetTenants;

/// <summary>Chiriașii flotei, pentru al doilea contract cu același om.</summary>
public sealed record GetTenantsQuery : IQuery<List<TenantDto>>;

internal sealed class GetTenantsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetTenantsQuery, List<TenantDto>>
{
    public async Task<Result<List<TenantDto>>> Handle(
        GetTenantsQuery query,
        CancellationToken cancellationToken)
    {
        List<Domain.Rentals.Tenant> tenants = await context.Tenants
            .AsNoTracking()
            .Where(t => t.OwnerUserId == userContext.UserId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return Result.Success(tenants.Select(GetRentalsQueryHandler.ToDto).ToList());
    }
}

using Application.Abstractions.Data;

using Application.Abstractions.Messaging;

using Domain.Payments;

using Microsoft.EntityFrameworkCore;

using SharedKernel;



namespace Application.Payments.Queries.GetServiceOrdersAdmin;



public sealed record GetServiceOrdersAdminQuery(string? Status = null) : IQuery<List<ServiceOrderAdminDto>>;



public sealed record ServiceOrderAdminDto(

    Guid Id,

    string ServiceKey,

    string ServiceTitle,

    string CustomerName,

    string CustomerEmail,

    string CustomerPhone,

    string Status,

    long? AmountBani,

    DateTime CreatedAtUtc,

    DateTime? PaidAtUtc);



internal sealed class GetServiceOrdersAdminQueryHandler(IApplicationDbContext context)

    : IQueryHandler<GetServiceOrdersAdminQuery, List<ServiceOrderAdminDto>>

{

    public async Task<Result<List<ServiceOrderAdminDto>>> Handle(

        GetServiceOrdersAdminQuery query,

        CancellationToken cancellationToken)

    {

        IQueryable<ServiceOrder> queryable = context.ServiceOrders.AsNoTracking();



        if (!string.IsNullOrWhiteSpace(query.Status)

            && Enum.TryParse<ServiceOrderStatus>(query.Status, ignoreCase: true, out ServiceOrderStatus status))

        {

            queryable = queryable.Where(o => o.Status == status);

        }



        List<ServiceOrderAdminDto> orders = await queryable

            .OrderByDescending(o => o.PaidAtUtc ?? o.CreatedAtUtc)

            .Select(o => new ServiceOrderAdminDto(

                o.Id,

                o.ServiceKey,

                o.ServiceTitle,

                o.CustomerName,

                o.CustomerEmail,

                o.CustomerPhone,

                o.Status.ToString(),

                o.AmountBani,

                o.CreatedAtUtc,

                o.PaidAtUtc))

            .ToListAsync(cancellationToken);



        return orders;

    }

}



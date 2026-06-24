using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Uber;

public sealed record GetUberDashboardQuery(
    string? Period,
    int? Year,
    int? Month) : IQuery<UberDashboardResponse>;

internal sealed class GetUberDashboardQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext) : IQueryHandler<GetUberDashboardQuery, UberDashboardResponse>
{
    public async Task<Result<UberDashboardResponse>> Handle(
        GetUberDashboardQuery query,
        CancellationToken cancellationToken)
    {
        string period = query.Period?.Trim().ToUpperInvariant() switch
        {
            "YEAR" => "year",
            "TOTAL" => "total",
            _ => "month"
        };

        DateTime now = DateTime.UtcNow;
        int? year = period is "month" or "year" ? query.Year ?? now.Year : null;
        int? month = period == "month" ? query.Month ?? now.Month : null;

        if (month is < 1 or > 12)
        {
            return Result.Failure<UberDashboardResponse>(
                Error.Problem("Uber.InvalidMonth", "Luna trebuie să fie între 1 și 12."));
        }

        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.UserId == userContext.UserId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pfa is null)
        {
            return new UberDashboardResponse(period, year, month, new UberStatsDto(0, 0, 0, 0, 0, 0, 0, 0), []);
        }

        return await UberDashboardProjector.GetDashboardAsync(context, pfa.Id, period, year, month, cancellationToken);
    }
}

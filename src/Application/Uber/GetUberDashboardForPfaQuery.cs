using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Uber;

/// <summary>
/// Aceleași date ca dashboardul clientului, dar pentru un PFA ales de operator —
/// panoul de import din Admin are nevoie de istoricul rapoartelor deja încărcate.
/// </summary>
public sealed record GetUberDashboardForPfaQuery(
    Guid PfaRegistrationId,
    string? Period,
    int? Year,
    int? Month) : IQuery<UberDashboardResponse>;

internal sealed class GetUberDashboardForPfaQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetUberDashboardForPfaQuery, UberDashboardResponse>
{
    public async Task<Result<UberDashboardResponse>> Handle(
        GetUberDashboardForPfaQuery query,
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

        bool exists = await context.PfaRegistrations
            .AsNoTracking()
            .AnyAsync(p => p.Id == query.PfaRegistrationId, cancellationToken);

        if (!exists)
        {
            return Result.Failure<UberDashboardResponse>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        return await UberDashboardProjector.GetDashboardAsync(
            context,
            query.PfaRegistrationId,
            period,
            year,
            month,
            cancellationToken);
    }
}

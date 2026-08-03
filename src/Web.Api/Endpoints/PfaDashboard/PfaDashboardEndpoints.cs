using System.Globalization;
using Application.Abstractions.Messaging;
using Application.PfaDashboard;
using SharedKernel;
using Web.Api.Extensions;
using Web.Api.Infrastructure;

namespace Web.Api.Endpoints.PfaDashboard;

internal sealed class PfaDashboardEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("pfa/dashboard")
            .RequireAuthorization()
            .WithTags("PfaDashboard");

        group.MapGet("summary", async (
            string? from,
            string? to,
            string? platform,
            string? payment,
            IQueryHandler<GetPfaDashboardSummaryQuery, PfaDashboardSummaryResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseRange(from, to, out DateOnly parsedFrom, out DateOnly parsedTo, out Error? error))
            {
                return CustomResults.Problem(Result.Failure(error!));
            }

            Result<PfaDashboardSummaryResponse> result = await handler.Handle(
                new GetPfaDashboardSummaryQuery(parsedFrom, parsedTo, platform, payment),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });

        group.MapGet("rides", async (
            string? from,
            string? to,
            string? platform,
            string? payment,
            int? page,
            int? pageSize,
            string? sort,
            string? q,
            IQueryHandler<GetPfaDashboardRidesQuery, PfaRidesPageResponse> handler,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseRange(from, to, out DateOnly parsedFrom, out DateOnly parsedTo, out Error? error))
            {
                return CustomResults.Problem(Result.Failure(error!));
            }

            Result<PfaRidesPageResponse> result = await handler.Handle(
                new GetPfaDashboardRidesQuery(
                    parsedFrom,
                    parsedTo,
                    platform,
                    payment,
                    page ?? 1,
                    pageSize ?? 5,
                    sort,
                    q),
                cancellationToken);

            return result.Match(Results.Ok, CustomResults.Problem);
        });
    }

    /// <summary>
    /// Fără interval explicit se răspunde pe luna curentă — linkul „/summary" simplu
    /// rămâne util, iar frontendul nu trebuie să ghicească un default.
    /// </summary>
    private static bool TryParseRange(
        string? from,
        string? to,
        out DateOnly parsedFrom,
        out DateOnly parsedTo,
        out Error? error)
    {
        error = null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        parsedFrom = new DateOnly(today.Year, today.Month, 1);
        parsedTo = parsedFrom.AddMonths(1).AddDays(-1);

        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!DateOnly.TryParse(from, CultureInfo.InvariantCulture, out DateOnly candidate))
            {
                error = Error.Problem("PfaDashboard.InvalidFrom", "Parametrul „from” nu este o dată validă (AAAA-LL-ZZ).");
                return false;
            }

            parsedFrom = candidate;
            parsedTo = new DateOnly(candidate.Year, candidate.Month, 1).AddMonths(1).AddDays(-1);
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            if (!DateOnly.TryParse(to, CultureInfo.InvariantCulture, out DateOnly candidate))
            {
                error = Error.Problem("PfaDashboard.InvalidTo", "Parametrul „to” nu este o dată validă (AAAA-LL-ZZ).");
                return false;
            }

            parsedTo = candidate;
        }

        return true;
    }
}

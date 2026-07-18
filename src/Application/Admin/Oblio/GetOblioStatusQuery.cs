using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using SharedKernel;

namespace Application.Admin.Oblio;

public sealed record GetOblioStatusQuery : IQuery<OblioStatusResponse>;

public sealed record OblioStatusResponse(
    bool Configured,
    string? Cif,
    string? SeriesName,
    bool ConnectionOk,
    string? CompanyName,
    IReadOnlyList<string> AvailableSeries,
    string? Error);

internal sealed class GetOblioStatusQueryHandler(IOblioService oblioService)
    : IQueryHandler<GetOblioStatusQuery, OblioStatusResponse>
{
    public async Task<Result<OblioStatusResponse>> Handle(
        GetOblioStatusQuery query,
        CancellationToken cancellationToken)
    {
        if (!oblioService.IsConfigured)
        {
            return new OblioStatusResponse(
                Configured: false,
                Cif: oblioService.Cif,
                SeriesName: oblioService.SeriesName,
                ConnectionOk: false,
                CompanyName: null,
                AvailableSeries: [],
                Error: "Lipsesc datele de configurare Oblio (ClientId, ClientSecret, Cif sau SeriesName).");
        }

        try
        {
            OblioConnectionInfo info = await oblioService.TestConnectionAsync(cancellationToken);

            return new OblioStatusResponse(
                Configured: true,
                Cif: oblioService.Cif,
                SeriesName: oblioService.SeriesName,
                ConnectionOk: true,
                CompanyName: info.CompanyName,
                AvailableSeries: info.InvoiceSeries,
                Error: oblioService.SeriesName is not null && !info.InvoiceSeries.Contains(oblioService.SeriesName)
                    ? $"Atenție: seria configurată \"{oblioService.SeriesName}\" nu există în Oblio. Serii disponibile: {string.Join(", ", info.InvoiceSeries)}."
                    : null);
        }
        catch (OblioApiException ex)
        {
            return new OblioStatusResponse(
                Configured: true,
                Cif: oblioService.Cif,
                SeriesName: oblioService.SeriesName,
                ConnectionOk: false,
                CompanyName: null,
                AvailableSeries: [],
                Error: ex.Message);
        }
    }
}

using Domain.Bolt;

namespace Application.Abstractions.Services;

public interface IBoltService
{
    Task<string> GetAccessTokenAsync(BoltIntegration integration, CancellationToken cancellationToken);
    Task<(int CompanyId, string CompanyName)> FetchCompanyIdAsync(string accessToken, CancellationToken cancellationToken);
    Task<List<BoltOrder>> FetchOrdersAsync(BoltIntegration integration, DateTime start, DateTime end, CancellationToken cancellationToken);
}

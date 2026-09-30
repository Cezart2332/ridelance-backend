using SharedKernel;

namespace Application.Abstractions.Services;

/// <summary>Comerciantul creat la FiscalLink. Numele e obligatoriu; restul, opțional.</summary>
public sealed record FiscalLinkNewClient(
    string Name,
    string? Cui,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Address);

/// <summary>Codul cu care clientul își activează casele de marcat în aplicația FiscalLink.</summary>
public sealed record FiscalLinkActivation(string ActivationCode, string ActivationLink, int RegisterCount);

/// <summary>O casă de marcat a clientului, cu starea și conectivitatea ei.</summary>
public sealed record FiscalLinkRegister(
    Guid Id,
    string? SerialNumber,
    string Status,
    bool IsOnline,
    bool AwaitingClientConsent,
    DateTime? ActivatedAtUtc);

/// <summary>
/// API-ul de integrator FiscalLink, partea de gestiune: clienți, coduri de activare, case de marcat.
/// Tipărirea (bonuri, rapoarte X/Z) folosește altă cheie și vine separat.
/// </summary>
public interface IFiscalLinkService
{
    Task<Result<Guid>> CreateClientAsync(FiscalLinkNewClient client, CancellationToken cancellationToken = default);

    Task<Result<FiscalLinkActivation>> GetActivationAsync(Guid clientId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<FiscalLinkRegister>>> ListRegistersAsync(Guid clientId, CancellationToken cancellationToken = default);
}

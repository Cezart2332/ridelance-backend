namespace Application.Abstractions.Services;

/// <summary>
/// Următorul cod public de închiriere: <c>RL-000123</c>.
/// </summary>
/// <remarks>
/// În spatele unui port pentru că numerotarea e treaba bazei de date, nu a aplicației. Un
/// <c>MAX(cod) + 1</c> calculat în handler dă același număr la două cereri simultane, iar a doua
/// pică pe indexul unic — sau, mai rău, dacă indexul lipsește, două contracte pleacă spre semnare
/// cu același număr.
/// </remarks>
public interface IRentalCodeGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}

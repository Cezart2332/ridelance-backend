namespace Application.Abstractions.Services;

/// <summary>
/// Codul poștal al unei localități, din nomenclatorul SIRUTA (localitati.dev). Satele și orașele
/// mici au un singur cod; la orașele mari codul e pe stradă, deci ce vine de aici e cel general
/// al localității, iar omul îl poate corecta.
/// </summary>
public interface IPostalCodeLookup
{
    /// <returns>Șase cifre, sau null când localitatea nu se găsește ori serviciul nu răspunde.</returns>
    Task<string?> FindAsync(string judet, string localitate, CancellationToken cancellationToken);
}

using SharedKernel;

namespace Application.Abstractions.Services;

/// <summary>
/// Trimite un SMS. Un singur mesaj, către un singur număr.
/// </summary>
/// <remarks>
/// Interfața nu spune prin cine: furnizorii de SMS se schimbă după preț și după livrabilitate în
/// România, iar codul care cere o confirmare n-are de ce să afle asta. Implementarea curentă e
/// Vonage; înlocuirea ei înseamnă o clasă și o linie în <c>DependencyInjection</c>.
/// </remarks>
public interface ISmsService
{
    /// <summary>
    /// Numărul se dă în format internațional (<c>+407…</c>). Eșecul e un
    /// <see cref="Result" />, nu o excepție: un SMS netrimis e un lucru despre care apelantul
    /// trebuie să-i spună omului, nu o defecțiune.
    /// </summary>
    Task<Result> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}

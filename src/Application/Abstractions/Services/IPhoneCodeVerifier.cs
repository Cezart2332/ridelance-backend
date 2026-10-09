using SharedKernel;

namespace Application.Abstractions.Services;

/// <summary>Verdictul unui cod de confirmare verificat la furnizor.</summary>
public enum PhoneCodeCheck
{
    Approved = 0,
    /// <summary>Cod greșit; verificarea rămâne deschisă pentru o nouă încercare.</summary>
    Wrong = 1,
    /// <summary>Nu mai există o verificare deschisă: a expirat, a fost consumată sau s-au epuizat încercările.</summary>
    Expired = 2,
}

/// <summary>
/// Confirmarea numărului de telefon făcută cap-coadă de un furnizor (Twilio Verify): el generează
/// codul, îl trimite de pe numerele lui și îl verifică. Nu avem nevoie de un număr de expeditor
/// propriu, iar codul nu trece prin baza noastră.
/// </summary>
/// <remarks>
/// Neconfigurat (<see cref="IsEnabled"/> fals), trimiterea și verificarea eșuează explicit, cu un
/// mesaj că SMS-ul nu e configurat — nu tăcut cu succes.
/// </remarks>
public interface IPhoneCodeVerifier
{
    bool IsEnabled { get; }

    /// <summary>Trimite un cod pe numărul dat, în format internațional (<c>+407…</c>).</summary>
    Task<Result> SendCodeAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Verifică codul tastat pentru numărul dat.</summary>
    Task<Result<PhoneCodeCheck>> CheckCodeAsync(string phoneNumber, string code, CancellationToken cancellationToken = default);
}

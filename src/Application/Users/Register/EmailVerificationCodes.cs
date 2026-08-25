using System.Security.Cryptography;
using System.Globalization;
using Domain.Users;

namespace Application.Users.Register;

/// <summary>
/// Emite codul de confirmare pe un cont.
/// </summary>
/// <remarks>
/// Stă separat de handler pentru că e folosit din două locuri: la înregistrare și la retrimitere.
/// </remarks>
public static class EmailVerificationCodes
{
    /// <summary>
    /// Pune un cod nou pe cont și îi resetează încercările.
    /// </summary>
    /// <remarks>
    /// Cifrele vin din generatorul criptografic, nu din <see cref="Random" />: un cod ghicibil
    /// dintr-un cod anterior n-ar mai confirma nimic.
    /// </remarks>
    public static string Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        int max = (int)Math.Pow(10, EmailVerification.CodeLength);
        string code = RandomNumberGenerator
            .GetInt32(max)
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(EmailVerification.CodeLength, '0');

        user.EmailVerificationCode = code;
        user.EmailVerificationCodeExpiresAtUtc = DateTime.UtcNow.Add(EmailVerification.CodeLifetime);
        user.EmailVerificationAttempts = 0;

        return code;
    }
}

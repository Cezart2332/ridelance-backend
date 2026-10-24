using System.Globalization;
using Domain.Users;
using Microsoft.Extensions.Configuration;

namespace Application.Users.AccountVerification;

/// <summary>Ce confirmare îi mai lipsește unui cont ca să poată folosi platforma.</summary>
public sealed record PendingVerification(bool Email, bool Phone)
{
    public static readonly PendingVerification None = new(false, false);

    public bool Any => Email || Phone;
}

/// <summary>
/// Regula confirmării contului, aplicată pe server. Înainte, codurile din email și SMS erau cerute
/// doar de pagina de după înregistrare: cine pleca de pe ea (înapoi la site, apoi în aplicație)
/// intra direct în onboarding, cu un cont neconfirmat.
/// </summary>
/// <remarks>
/// Comutatoarele sunt aceleași ca pentru pagina de confirmare (<c>Verification:RequireEmail</c>,
/// <c>Verification:RequirePhone</c>), deci site-ul de test rămâne fără coduri. Conturile create
/// înainte de <see cref="EnforceFromKey"/> nu sunt atinse: s-au făcut când confirmarea era
/// opțională și ar fi rămas pe dinafară peste noapte. Echipa (admin, contabil) nu trece prin ea.
/// </remarks>
public sealed class AccountVerificationGate(IConfiguration configuration)
{
    public const string RequireEmailKey = "Verification:RequireEmail";
    public const string RequirePhoneKey = "Verification:RequirePhone";
    public const string EnforceFromKey = "Verification:EnforceForAccountsCreatedFromUtc";

    /// <summary>Ziua în care regula a început să se aplice pe server.</summary>
    public static readonly DateTime DefaultEnforceFromUtc = new(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);

    public bool EmailRequired => IsOn(RequireEmailKey);

    public bool PhoneRequired => IsOn(RequirePhoneKey);

    public PendingVerification Pending(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Pending(user.Role, user.CreatedAtUtc, user.IsEmailVerified, user.IsPhoneVerified);
    }

    public PendingVerification Pending(UserRole role, DateTime createdAtUtc, bool emailVerified, bool phoneVerified)
    {
        if (role is UserRole.Admin or UserRole.Contabil || createdAtUtc < EnforceFromUtc)
        {
            return PendingVerification.None;
        }

        return new PendingVerification(EmailRequired && !emailVerified, PhoneRequired && !phoneVerified);
    }

    private DateTime EnforceFromUtc =>
        DateTime.TryParse(configuration[EnforceFromKey], CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
            ? value
            : DefaultEnforceFromUtc;

    private bool IsOn(string key) => bool.TryParse(configuration[key], out bool on) && on;
}

namespace Web.Api.Endpoints.Users;

/// <summary>
/// Aplicația mobilă (Capacitor) nu poate folosi cookie-ul de refresh.
///
/// Pagina rulează din <c>https://localhost</c> (Android) sau <c>capacitor://localhost</c> (iOS), deci
/// orice cerere către API e cross-site, iar un cookie <c>SameSite=Lax</c> nu pleacă. Fără alt drum,
/// omul ar fi delogat la fiecare deschidere a aplicației. Aplicația se anunță prin antet, păstrează
/// refresh token-ul în spațiul ei de stocare și îl trimite înapoi tot prin antet.
///
/// Browserul rămâne pe cookie <c>HttpOnly</c>: tokenul iese în corpul răspunsului doar la login cu
/// antetul de client nativ (cere parola) sau la un refresh făcut chiar cu tokenul din antet — deci
/// un script injectat în site, care nu cunoaște valoarea cookie-ului, nu are cum să-l obțină.
/// </summary>
internal static class NativeClient
{
    private const string ClientHeader = "X-Ridelance-Client";
    private const string RefreshTokenHeader = "X-Refresh-Token";

    public static bool IsNative(HttpRequest request) =>
        string.Equals(request.Headers[ClientHeader], "native", StringComparison.Ordinal);

    /// <summary>Refresh token-ul trimis de aplicație, sau null dacă cererea nu vine de la ea.</summary>
    public static string? RefreshTokenFromHeader(HttpRequest request)
    {
        if (!IsNative(request))
        {
            return null;
        }

        string? token = request.Headers[RefreshTokenHeader];
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}

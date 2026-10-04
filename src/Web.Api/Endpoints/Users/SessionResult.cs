using Application.Users.Login;

namespace Web.Api.Endpoints.Users;

/// <summary>Răspunsul unei autentificări: cookie-ul de refresh și corpul pentru frontend (sau pasul 2FA).</summary>
internal static class SessionResult
{
    public static IResult Write(HttpContext httpContext, LoginResponse session, object? extra = null)
    {
        // Pasul 2FA: nicio sesiune încă, doar tokenul scurt al pasului.
        if (session.IsChallenge)
        {
            return Results.Ok(new { twoFactor = session.TwoFactor, challengeToken = session.ChallengeToken, role = session.Role });
        }

        httpContext.Response.Cookies.Append("refreshToken", session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/",
        });

        // Aplicația mobilă nu primește cookie-ul (vezi NativeClient): tokenul de refresh îi vine în corp.
        bool native = NativeClient.IsNative(httpContext.Request);
        return Results.Ok(new
        {
            accessToken = session.AccessToken,
            role = session.Role,
            userId = session.UserId,
            refreshToken = native ? session.RefreshToken : null,
            extra,
        });
    }
}

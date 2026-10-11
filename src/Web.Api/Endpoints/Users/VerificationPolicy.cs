namespace Web.Api.Endpoints.Users;

/// <summary>
/// <c>GET /users/verification-policy</c> — dacă mediul acesta cere codul de pe email și pe cel
/// din SMS la înregistrare.
/// </summary>
/// <remarks>
/// Regula vine din configurarea serverului (<c>REQUIRE_EMAIL_VERIFICATION</c>,
/// <c>REQUIRE_PHONE_VERIFICATION</c>), nu din build-ul interfeței: aceeași imagine rulează și pe
/// site-ul de producție, unde codurile sunt obligatorii, și pe cel de test, unde nu sunt.
///
/// Anonim: pagina de confirmare o cere imediat după crearea contului.
/// </remarks>
internal sealed class VerificationPolicy : IEndpoint
{
    public const string RequireEmailKey = "Verification:RequireEmail";
    public const string RequirePhoneKey = "Verification:RequirePhone";

    public sealed record Response(bool EmailRequired, bool PhoneRequired);

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("users/verification-policy", (IConfiguration configuration) =>
            Results.Ok(new Response(IsOn(configuration, RequireEmailKey), IsOn(configuration, RequirePhoneKey))))
        .AllowAnonymous()
        .WithTags(Tags.Users);
    }

    private static bool IsOn(IConfiguration configuration, string key) =>
        bool.TryParse(configuration[key], out bool required) && required;
}

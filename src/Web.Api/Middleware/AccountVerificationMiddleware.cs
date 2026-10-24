using System.Security.Claims;
using Application.Abstractions.Data;
using Application.Users.AccountVerification;
using Domain.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Web.Api.Middleware;

/// <summary>
/// Un cont care nu și-a confirmat emailul sau telefonul (când mediul le cere) nu poate folosi
/// endpointurile protejate, în afară de cele cu care se face confirmarea. Interfața îl trimite
/// înapoi la pagina de confirmare; aici e garanția, oricum ar ajunge cererea.
/// </summary>
public sealed class AccountVerificationMiddleware(RequestDelegate next)
{
    public const string ErrorCode = "Account.VerificationRequired";

    public async Task InvokeAsync(HttpContext http, IApplicationDbContext context, AccountVerificationGate gate)
    {
        Endpoint? endpoint = http.GetEndpoint();
        bool protectedEndpoint = endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

        if (!protectedEndpoint
            || !gate.EmailRequired && !gate.PhoneRequired
            || IsVerificationPath(http.Request.Path)
            || http.User.Identity?.IsAuthenticated != true
            || !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
        {
            await next(http);
            return;
        }

        var user = await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role, u.CreatedAtUtc, u.EmailVerifiedAtUtc, u.PhoneVerifiedAtUtc })
            .SingleOrDefaultAsync(http.RequestAborted);

        PendingVerification pending = user is null
            ? PendingVerification.None
            : gate.Pending(user.Role, user.CreatedAtUtc, user.EmailVerifiedAtUtc is not null, user.PhoneVerifiedAtUtc is not null);

        if (pending.Any)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(
                new
                {
                    code = ErrorCode,
                    detail = "Confirmă-ți adresa de email și numărul de telefon ca să continui.",
                    pendingEmail = pending.Email,
                    pendingPhone = pending.Phone,
                },
                http.RequestAborted);
            return;
        }

        await next(http);
    }

    /// <summary>Ce rămâne deschis unui cont neconfirmat: confirmarea, profilul, sesiunea.</summary>
    private static bool IsVerificationPath(PathString path) =>
        path.StartsWithSegments("/users/phone")
        || path == "/users/profile" || path == "/users/logout" || path == "/users/refresh-token"
        || path == "/users/verify-email" || path == "/users/resend-verification"
        || path == "/users/verification-policy";
}

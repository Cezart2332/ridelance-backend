using Application.Abstractions.Data;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Web.Api.Middleware;

public sealed class FleetAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, IApplicationDbContext context)
    {
        if (http.User.Identity?.IsAuthenticated != true
            || !Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
        {
            await next(http);
            return;
        }
        User? user = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, http.RequestAborted);
        if (user is null || user.Role != UserRole.CarPoster || !user.FleetOnboardingRequired || IsSetupPath(http.Request.Path))
        {
            await next(http);
            return;
        }
        bool allowed = user.FleetOnboarding.CompletedAtUtc is not null
            && await context.UserSubscriptions.AnyAsync(s => s.UserId == userId && s.Plan == SubscriptionPlan.Fleet
                && s.Status == SubscriptionStatus.Active, http.RequestAborted);
        if (!allowed)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new { code = "Fleet.OnboardingRequired", detail = "Finalizează configurarea flotei și plata abonamentului." }, http.RequestAborted);
            return;
        }
        await next(http);
    }

    private static bool IsSetupPath(PathString path) =>
        path.StartsWithSegments("/fleet-onboarding") || path.StartsWithSegments("/users/phone")
        || path == "/users/profile" || path == "/users/logout" || path == "/users/refresh-token"
        || path == "/users/verify-email" || path == "/users/resend-verification"
        || path == "/users/change-password" || path == "/users/push/subscribe"
        || path.StartsWithSegments("/bank/connection")
        || path == "/bank/institutions" || path.StartsWithSegments("/invoices/oblio")
        || path == "/payments/subscription" || path == "/payments/session-status"
        || path.StartsWithSegments("/notifications");
}

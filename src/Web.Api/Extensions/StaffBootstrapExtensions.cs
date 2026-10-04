using Application.Abstractions.Messaging;
using Application.Users.Staff;
using SharedKernel;

namespace Web.Api.Extensions;

/// <summary>
/// La pornire, după migrații: invitația primului admin (doar pe o platformă fără admin, din
/// <c>Bootstrap:AdminEmail</c>) și resetarea de urgență a 2FA-ului (<c>Security:ResetTwoFactorEmail</c>).
/// </summary>
internal static class StaffBootstrapExtensions
{
    public static async Task BootstrapStaffAsync(this WebApplication app)
    {
        using IServiceScope scope = app.Services.CreateScope();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StaffBootstrap");
        IConfiguration configuration = app.Configuration;

        ICommandHandler<EnsureAdminBootstrapCommand, string?> bootstrap =
            scope.ServiceProvider.GetRequiredService<ICommandHandler<EnsureAdminBootstrapCommand, string?>>();
        Result<string?> invitation = await bootstrap.Handle(new EnsureAdminBootstrapCommand(configuration["Bootstrap:AdminEmail"]), CancellationToken.None);
        if (invitation.IsFailure)
        {
            logger.LogError("Primul admin nu a putut fi invitat: {Error}", invitation.Error.Description);
        }
        else if (invitation.Value is { } link)
        {
            // Rezerva pentru cazul în care emailul nu ajunge: linkul se vede doar în logurile serverului.
            logger.LogWarning("Platforma nu are niciun admin. Link de creare a contului de proprietar (24 h, o singură folosire): {Link}", link);
        }

        ICommandHandler<ResetTwoFactorFromConfigCommand, bool> reset =
            scope.ServiceProvider.GetRequiredService<ICommandHandler<ResetTwoFactorFromConfigCommand, bool>>();
        string? resetEmail = configuration["Security:ResetTwoFactorEmail"];
        Result<bool> cleared = await reset.Handle(new ResetTwoFactorFromConfigCommand(resetEmail), CancellationToken.None);
        if (cleared.IsSuccess && cleared.Value)
        {
            logger.LogWarning("2FA resetat din configurare pentru {Email}. Scoate Security:ResetTwoFactorEmail din variabilele de mediu.", resetEmail);
        }
    }
}

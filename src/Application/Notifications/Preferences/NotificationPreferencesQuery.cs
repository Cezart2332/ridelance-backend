using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Notifications.Preferences;

/// <param name="Group">`operational` sau `commercial` — separarea cerută de spec §10.5.</param>
public sealed record NotificationPreferenceItem(string Category, string Label, string Group, bool Enabled);

public sealed record NotificationPreferencesResponse(List<NotificationPreferenceItem> Items);

public sealed record GetNotificationPreferencesQuery : IQuery<NotificationPreferencesResponse>;

internal static class NotificationPreferenceLabels
{
    public static string For(NotificationCategory category) => category switch
    {
        NotificationCategory.DocumentExpiry => "Documente care expiră",
        NotificationCategory.TaxesAndDeadlines => "Taxe și termene",
        NotificationCategory.AccountantMessages => "Mesaje de la contabil",
        NotificationCategory.PlatformSyncIssues => "Probleme de sincronizare Bolt/Uber",
        NotificationCategory.RidelanceUpdates => "Notificări RIDElance",
        _ => "Oferte și beneficii",
    };
}

internal sealed class GetNotificationPreferencesQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesResponse>
{
    public async Task<Result<NotificationPreferencesResponse>> Handle(
        GetNotificationPreferencesQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        Dictionary<NotificationCategory, bool> stored = await context.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Category, p => p.Enabled, cancellationToken);

        // Se întorc toate categoriile, nu doar cele salvate: ecranul de setări trebuie să arate
        // lista completă, iar ce lipsește din bază înseamnă „activ".
        var items = Enum.GetValues<NotificationCategory>()
            .Select(category => new NotificationPreferenceItem(
                category.ToString(),
                NotificationPreferenceLabels.For(category),
                NotificationPreference.IsCommercial(category) ? "commercial" : "operational",
                stored.GetValueOrDefault(category, true)))
            .ToList();

        return new NotificationPreferencesResponse(items);
    }
}

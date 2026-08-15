using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Notifications.Preferences;

public sealed record NotificationPreferenceUpdate(string Category, bool Enabled);

public sealed record UpdateNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceUpdate> Items)
    : ICommand<NotificationPreferencesResponse>;

internal sealed class UpdateNotificationPreferencesCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesResponse> readHandler)
    : ICommandHandler<UpdateNotificationPreferencesCommand, NotificationPreferencesResponse>
{
    public async Task<Result<NotificationPreferencesResponse>> Handle(
        UpdateNotificationPreferencesCommand command,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        List<NotificationPreference> existing = await context.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (NotificationPreferenceUpdate update in command.Items)
        {
            if (!Enum.TryParse(update.Category, ignoreCase: true, out NotificationCategory category))
            {
                return Result.Failure<NotificationPreferencesResponse>(
                    Error.Problem("Notifications.InvalidCategory", $"Categoria „{update.Category}” nu există."));
            }

            NotificationPreference? preference = existing.SingleOrDefault(p => p.Category == category);

            if (preference is null)
            {
                preference = new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Category = category,
                };
                context.NotificationPreferences.Add(preference);
                existing.Add(preference);
            }

            preference.Enabled = update.Enabled;
            preference.UpdatedAtUtc = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        return await readHandler.Handle(new GetNotificationPreferencesQuery(), cancellationToken);
    }
}

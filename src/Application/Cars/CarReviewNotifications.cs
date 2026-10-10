using Application.Abstractions.Data;
using Domain.Cars;
using Domain.Notifications;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Cars;

/// <summary>
/// Anunțul unei firme intră la validare: fiecare admin primește o notificare care deschide tabul
/// „Validare”. Fără ea, anunțul aștepta nevăzut până intra cineva din întâmplare acolo.
/// </summary>
internal static class CarReviewNotifications
{
    /// <summary>Cheia unei mașini: cât timp adminul n-a citit-o pe cea veche, editările repetate nu mai adaugă altele.</summary>
    private static string DedupeKey(Guid carId) => $"car-review:{carId}";

    public static async Task AddAsync(IApplicationDbContext context, Car car, bool edited, CancellationToken cancellationToken)
    {
        string dedupeKey = DedupeKey(car.Id);
        List<Guid> alreadyWaiting = await context.Notifications
            .Where(n => n.DedupeKey == dedupeKey && !n.IsRead && !n.IsDismissed)
            .Select(n => n.UserId)
            .ToListAsync(cancellationToken);

        List<Guid> adminIds = await context.Users
            .Where(u => u.Role == UserRole.Admin && u.DeletedAtUtc == null)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        string what = $"{car.Brand} {car.Model}".Trim();
        foreach (Guid adminId in adminIds.Except(alreadyWaiting))
        {
            context.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                Text = edited
                    ? $"Anunțul „{what}” a fost modificat de firmă și așteaptă din nou validarea."
                    : $"O firmă a adăugat un anunț nou, „{what}”, care așteaptă validarea.",
                Type = NotificationTypes.CarListingReview,
                RelatedUserId = car.PostedByUserId,
                DedupeKey = dedupeKey,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
    }
}

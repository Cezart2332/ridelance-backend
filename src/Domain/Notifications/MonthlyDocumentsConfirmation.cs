using Domain.Users;
using SharedKernel;

namespace Domain.Notifications;

/// <summary>
/// PFA-ul a spus că a terminat cu documentele unei luni („Nu am bonuri de încărcat”): luna e
/// completă din partea lui, iar amintirile din 4 și 5 nu mai pleacă.
/// </summary>
public sealed class MonthlyDocumentsConfirmation : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime ConfirmedAtUtc { get; set; }

    public User User { get; set; } = null!;
}

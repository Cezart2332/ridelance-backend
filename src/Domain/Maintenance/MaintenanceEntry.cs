using SharedKernel;

namespace Domain.Maintenance;

/// <summary>
/// O intervenție de service pe o mașină din flotă: ce s-a făcut, când, la ce kilometraj, cât a costat.
/// </summary>
/// <remarks>
/// Kilometrajul se reține pe intervenție, nu doar data, fiindcă reviziile se programează în km, iar
/// „la 15.000 km de la ultima" nu se poate calcula dintr-o dată calendaristică.
/// </remarks>
public sealed class MaintenanceEntry : Entity
{
    public Guid Id { get; set; }

    public Guid CarId { get; set; }

    /// <summary>Proprietarul, pentru filtrare fără join și pentru verificarea accesului.</summary>
    public Guid OwnerUserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }

    /// <summary>Data intervenției. Viitoare = programare, trecută = istoric.</summary>
    public DateTime PerformedAtUtc { get; set; }

    /// <summary>Kilometrajul la momentul intervenției. `null` când nu a fost notat.</summary>
    public int? Mileage { get; set; }

    /// <summary>Costul în bani. Zero e o valoare validă — o intervenție în garanție.</summary>
    public long CostBani { get; set; }

    /// <summary>Reminder pe dată. Independent de cel pe kilometraj: pot exista amândouă.</summary>
    public DateTime? ReminderDateUtc { get; set; }

    /// <summary>Reminder la kilometraj.</summary>
    public int? ReminderMileage { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

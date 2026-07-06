namespace Domain.Office;

/// <summary>A 30-minute visit booked at the RIDElance office.</summary>
public sealed class OfficeAppointment
{
    public Guid Id { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public int DurationMinutes { get; set; } = 30;

    // Visitor info
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    /// <summary>Set when the booking was made from inside the app by a logged-in user.</summary>
    public Guid? UserId { get; set; }

    public OfficeAppointmentStatus Status { get; set; } = OfficeAppointmentStatus.Confirmed;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum OfficeAppointmentStatus
{
    Confirmed = 0,
    Cancelled = 1,
}

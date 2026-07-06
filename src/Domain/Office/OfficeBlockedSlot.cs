namespace Domain.Office;

/// <summary>A slot (or whole day, when StartTime is null) blocked by an admin.</summary>
public sealed class OfficeBlockedSlot
{
    public Guid Id { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>Null blocks the entire day.</summary>
    public TimeOnly? StartTime { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

namespace Domain.Office;

/// <summary>Weekly opening-hours template for the office, one row per weekday.</summary>
public sealed class OfficeScheduleDay
{
    public Guid Id { get; set; }

    public DayOfWeek Day { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
}

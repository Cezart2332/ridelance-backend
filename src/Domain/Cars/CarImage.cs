namespace Domain.Cars;

public sealed class CarImage
{
    public Guid Id { get; set; }
    public Guid CarId { get; set; }
    public string Url { get; set; } = string.Empty;      // relative path: /uploads/cars/{filename}
    public string FileName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Car Car { get; set; } = null!;
}

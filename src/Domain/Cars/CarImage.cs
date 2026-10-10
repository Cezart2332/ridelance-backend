namespace Domain.Cars;

public sealed class CarImage
{
    public Guid Id { get; set; }
    public Guid CarId { get; set; }
    public string Url { get; set; } = string.Empty;      // relative path: /uploads/cars/{filename}
    public string FileName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Când a trecut poza prin blurarea numărului de înmatriculare. <c>null</c> = poza e cea
    /// încărcată, cu numărul la vedere. Se blurează doar pentru mașinile cu opțiunea „număr ascuns”
    /// plătită: la încărcare dacă era deja plătită, altfel de jobul care rulează după plată.
    /// </summary>
    public DateTime? PlateBlurredAtUtc { get; set; }

    // Navigation
    public Car Car { get; set; } = null!;
}

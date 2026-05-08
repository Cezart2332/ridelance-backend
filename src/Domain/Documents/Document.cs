using Domain.Users;
using SharedKernel;

namespace Domain.Documents;

public sealed class Document : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? PfaRegistrationId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DocumentCategory Category { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public string EncryptedFilePath { get; set; } = string.Empty;
    public string EncryptionIv { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}

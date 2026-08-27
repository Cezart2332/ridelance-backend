using Application.Abstractions.Messaging;
using Domain.Documents;

namespace Application.Documents.Upload;

public sealed record UploadDocumentCommand(
    Guid UserId,
    Guid? PfaRegistrationId,
    DocumentCategory Category,
    string FileName,
    string ContentType,
    Stream FileStream,
    long FileSize,
    DateTime? ExpiresAtUtc,
    /// <summary>Mașina din flotă, când documentul e al ei.</summary>
    Guid? CarId = null) : ICommand<Guid>;

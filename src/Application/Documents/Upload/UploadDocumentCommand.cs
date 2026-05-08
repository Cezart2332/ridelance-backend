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
    long FileSize) : ICommand<Guid>;

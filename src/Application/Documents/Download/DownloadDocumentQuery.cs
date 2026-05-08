using Application.Abstractions.Messaging;

namespace Application.Documents.Download;

public sealed record DownloadDocumentQuery(Guid DocumentId, Guid RequestingUserId)
    : IQuery<DownloadDocumentResponse>;

public sealed record DownloadDocumentResponse(
    Stream FileStream,
    string ContentType,
    string FileName);

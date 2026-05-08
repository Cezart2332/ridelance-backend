using Application.Abstractions.Messaging;
using Domain.Documents;

namespace Application.Documents.UpdateStatus;

public sealed record UpdateDocumentStatusCommand(
    Guid DocumentId,
    Guid RequestingUserId,
    DocumentStatus Status) : ICommand;

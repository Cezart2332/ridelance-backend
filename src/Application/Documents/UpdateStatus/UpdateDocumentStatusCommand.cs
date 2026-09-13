using Application.Abstractions.Messaging;
using Domain.Documents;

namespace Application.Documents.UpdateStatus;

/// <param name="Note">Motivul, la respingere. Se afișează șoferului lângă document.</param>
public sealed record UpdateDocumentStatusCommand(
    Guid DocumentId,
    Guid RequestingUserId,
    DocumentStatus Status,
    string? Note = null) : ICommand;

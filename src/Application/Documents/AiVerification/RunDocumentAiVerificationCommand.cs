using Application.Abstractions.Messaging;

namespace Application.Documents.AiVerification;

public sealed record RunDocumentAiVerificationCommand(Guid DocumentId) : ICommand;

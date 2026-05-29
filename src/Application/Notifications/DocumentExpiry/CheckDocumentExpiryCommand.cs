using Application.Abstractions.Messaging;

namespace Application.Notifications.DocumentExpiry;

/// <summary>
/// Triggers the daily document-expiry check.
/// Finds documents expiring in exactly 30 or 7 days and sends notifications.
/// </summary>
public sealed record CheckDocumentExpiryCommand : ICommand;

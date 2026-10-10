namespace Application.Abstractions.Services;

public sealed record CreateMailboxRequest(string Name, string LocalPart, string Password);

public sealed record MailboxResult(string LocalPart, string Address);

public sealed record CreateIdentityRequest(string Name, string LocalPart, string Password);

public sealed record IdentityResult(string LocalPart, string Address);

/// <param name="StorageGb">Stocarea folosită pe domeniu, în GB.</param>
public sealed record MailboxUsage(int IncomingToday, int OutgoingToday, decimal StorageGb);

/// <summary>Furnizorul a refuzat sau n-a răspuns. Mesajul nu conține niciodată parole.</summary>
public sealed class MailboxProviderException : Exception
{
    public MailboxProviderException()
    {
    }

    public MailboxProviderException(string message)
        : base(message)
    {
    }

    public MailboxProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Furnizorul de mailbox-uri, văzut din aplicație (același tipar ca <see cref="IBankDataProvider"/>).
/// Ștergerea mailbox-ului lipsește intenționat: mailbox-urile nu se șterg niciodată din aplicație.
/// </summary>
public interface IMailboxProvider
{
    Task<bool> MailboxExistsAsync(string localPart, CancellationToken ct);

    Task<MailboxResult> CreateMailboxAsync(CreateMailboxRequest req, CancellationToken ct);

    Task SetMailboxPasswordAsync(string localPart, string password, CancellationToken ct);

    Task SetRecoveryEmailAsync(string localPart, string email, CancellationToken ct);

    Task<IdentityResult> CreateIdentityAsync(string mailboxLocalPart, CreateIdentityRequest req, CancellationToken ct);

    /// <summary>O identitate care nu (mai) există nu e o eroare: rezultatul cerut e deja atins.</summary>
    Task DeleteIdentityAsync(string mailboxLocalPart, string identityLocalPart, CancellationToken ct);

    Task<MailboxUsage> GetDomainUsageAsync(CancellationToken ct);
}

using System.Collections.Concurrent;
using Application.Abstractions.Services;
using Application.Mailboxes;
using Microsoft.Extensions.Options;

namespace Infrastructure.Mailboxes;

/// <summary>
/// Furnizorul în memorie (<c>MAILBOX_PROVIDER=Fake</c>): dezvoltare și teste, fără niciun apel la
/// Migadu. Se poartă ca cel real acolo unde contează: nu acceptă două mailbox-uri cu aceeași adresă.
/// </summary>
public sealed class FakeMailboxProvider(IOptions<MailboxOptions> options) : IMailboxProvider
{
    private readonly ConcurrentDictionary<string, FakeMailbox> _mailboxes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, FakeMailbox> Mailboxes => _mailboxes;

    public Task<bool> MailboxExistsAsync(string localPart, CancellationToken ct) =>
        Task.FromResult(_mailboxes.ContainsKey(localPart));

    public Task<MailboxResult> CreateMailboxAsync(CreateMailboxRequest req, CancellationToken ct)
    {
        if (!_mailboxes.TryAdd(req.LocalPart, new FakeMailbox(req.Name, req.Password)))
        {
            throw new MailboxProviderException($"Mailbox-ul „{req.LocalPart}” există deja.");
        }

        return Task.FromResult(new MailboxResult(req.LocalPart, $"{req.LocalPart}@{options.Value.Domain}"));
    }

    public Task SetMailboxPasswordAsync(string localPart, string password, CancellationToken ct)
    {
        Get(localPart).Password = password;
        return Task.CompletedTask;
    }

    public Task SetRecoveryEmailAsync(string localPart, string email, CancellationToken ct)
    {
        Get(localPart).RecoveryEmail = email;
        return Task.CompletedTask;
    }

    public Task<IdentityResult> CreateIdentityAsync(string mailboxLocalPart, CreateIdentityRequest req, CancellationToken ct)
    {
        if (!Get(mailboxLocalPart).Identities.TryAdd(req.LocalPart, req.Password))
        {
            throw new MailboxProviderException($"Identitatea „{req.LocalPart}” există deja.");
        }

        return Task.FromResult(new IdentityResult(req.LocalPart, $"{req.LocalPart}@{options.Value.Domain}"));
    }

    public Task DeleteIdentityAsync(string mailboxLocalPart, string identityLocalPart, CancellationToken ct)
    {
        Get(mailboxLocalPart).Identities.TryRemove(identityLocalPart, out _);
        return Task.CompletedTask;
    }

    public Task<MailboxUsage> GetDomainUsageAsync(CancellationToken ct) =>
        Task.FromResult(new MailboxUsage(0, 0, 0));

    private FakeMailbox Get(string localPart) =>
        _mailboxes.TryGetValue(localPart, out FakeMailbox? mailbox)
            ? mailbox
            : throw new MailboxProviderException($"Mailbox-ul „{localPart}” nu există.");
}

public sealed class FakeMailbox(string name, string password)
{
    public string Name { get; } = name;
    public string Password { get; set; } = password;
    public string? RecoveryEmail { get; set; }
    public ConcurrentDictionary<string, string> Identities { get; } = new(StringComparer.OrdinalIgnoreCase);
}

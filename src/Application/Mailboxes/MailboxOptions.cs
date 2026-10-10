using System.Security.Cryptography;
using Application.Abstractions.Security;

namespace Application.Mailboxes;

/// <summary>
/// Configurarea adreselor operaționale (secțiunea <c>Mailbox</c>; în mediu: <c>MIGADU_*</c>,
/// <c>MAILBOX_*</c>). Niciodată în frontend sau în git.
/// </summary>
public sealed class MailboxOptions
{
    public const string SectionName = "Mailbox";

    public const string MigaduProvider = "Migadu";
    public const string FakeProvider = "Fake";

    /// <summary><c>Migadu</c> sau <c>Fake</c> (în memorie, pentru dezvoltare și teste).</summary>
    public string Provider { get; set; } = FakeProvider;

    /// <summary>Emailul contului Migadu.</summary>
    public string? ApiUser { get; set; }

    /// <summary>Cheia din Migadu → My Account → API Keys.</summary>
    public string? ApiKey { get; set; }

    public string Domain { get; set; } = "pfa.ridelance.ro";

    /// <summary>Limitele zilnice ale planului Migadu curent.</summary>
    public int DailyIncomingLimit { get; set; } = 200;
    public int DailyOutgoingLimit { get; set; } = 20;

    /// <summary>Cheia cu care se criptează parolele stocate.</summary>
    public string? CredentialsKey { get; set; }

    public bool UsesMigadu => string.Equals(Provider, MigaduProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Se pot crea adrese: Fake merge mereu; Migadu are nevoie de cont, cheie API și cheia de criptare.
    /// Până atunci nimic nu intră singur în coadă, ca fiecare client ajuns la pas să nu producă un eșec.
    /// </summary>
    public bool CanProvision =>
        !UsesMigadu
        || !string.IsNullOrWhiteSpace(ApiUser) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(CredentialsKey);
}

/// <summary>Criptarea parolelor de mailbox, cu cheia lor (<c>MAILBOX_CREDENTIALS_KEY</c>).</summary>
public interface IMailboxCredentialProtector : ISecretProtector;

/// <summary>Setările pe care agentul le pune în Thunderbird și care intră în documentul de predare.</summary>
public static class MailboxServers
{
    public const string ImapHost = "imap.migadu.com";
    public const int ImapPort = 993;
    public const string SmtpHost = "smtp.migadu.com";
    public const int SmtpPort = 465;
    public const string Security = "SSL/TLS";
    public const string Webmail = "https://webmail.migadu.com";
}

public static class MailboxPasswords
{
    public const int Length = 28;

    // Fără caractere care se încurcă între ele la copiere (0/O, 1/l/I) și fără simboluri pe care
    // unii clienți de email le strică în URL-uri de configurare.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_";

    /// <summary>Parolă din generatorul criptografic, cu cel puțin 24 de caractere.</summary>
    public static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}

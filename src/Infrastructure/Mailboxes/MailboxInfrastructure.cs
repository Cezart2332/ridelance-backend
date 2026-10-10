using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Messaging;
using Application.Mailboxes;
using Domain.Mailboxes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Infrastructure.Mailboxes;

/// <summary>
/// Parolele de mailbox, criptate AES-GCM cu cheia lor (<c>MAILBOX_CREDENTIALS_KEY</c>), separată de
/// cheia generală a aplicației: cine o are pe una nu le poate citi pe celelalte.
/// </summary>
internal sealed class MailboxCredentialProtector(IOptions<MailboxOptions> options) : IMailboxCredentialProtector
{
    /// <summary>Doar cu furnizorul Fake: parolele unor mailbox-uri care nu există nu au ce proteja.</summary>
    private const string FakeProviderKey = "ridelance-fake-mailbox-provider";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Protect(string plainText)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] plaintext = Encoding.UTF8.GetBytes(plainText);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(Key(), TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }

    public string Unprotect(string protectedText)
    {
        byte[] payload = Convert.FromBase64String(protectedText);
        byte[] plaintext = new byte[payload.Length - NonceSize - TagSize];

        using var aes = new AesGcm(Key(), TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] Key()
    {
        MailboxOptions settings = options.Value;
        if (!string.IsNullOrWhiteSpace(settings.CredentialsKey))
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(settings.CredentialsKey));
        }

        // Cu Migadu, fără cheie nu criptăm nimic: o cheie implicită ar fi o parolă scrisă în cod.
        return settings.UsesMigadu
            ? throw new InvalidOperationException("MAILBOX_CREDENTIALS_KEY lipsește: parolele de mailbox nu pot fi criptate.")
            : SHA256.HashData(Encoding.UTF8.GetBytes(FakeProviderKey));
    }
}

internal sealed class ClientMailboxConfiguration : IEntityTypeConfiguration<ClientMailbox>
{
    public void Configure(EntityTypeBuilder<ClientMailbox> builder)
    {
        builder.HasKey(m => m.Id);

        // Un singur mailbox per client și o singură adresă per mailbox.
        builder.HasIndex(m => m.UserId).IsUnique();
        builder.HasIndex(m => m.LocalPart).IsUnique().HasFilter("local_part IS NOT NULL");
        builder.HasIndex(m => m.PfaRegistrationId);
        builder.HasIndex(m => m.Status);

        builder.Property(m => m.Address).HasMaxLength(320);
        builder.Property(m => m.LocalPart).HasMaxLength(128);
        builder.Property(m => m.MailboxPasswordEncrypted).HasMaxLength(512);
        builder.Property(m => m.OpsIdentityAddress).HasMaxLength(320);
        builder.Property(m => m.OpsIdentityLocalPart).HasMaxLength(160);
        builder.Property(m => m.OpsIdentityPasswordEncrypted).HasMaxLength(512);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(m => m.LastError).HasMaxLength(1024);

        builder.HasOne(m => m.PfaRegistration)
            .WithMany()
            .HasForeignKey(m => m.PfaRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.AuditLogs)
            .WithOne(l => l.Mailbox)
            .HasForeignKey(l => l.ClientMailboxId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ClientMailboxAuditLogConfiguration : IEntityTypeConfiguration<ClientMailboxAuditLog>
{
    public void Configure(EntityTypeBuilder<ClientMailboxAuditLog> builder)
    {
        builder.HasKey(l => l.Id);
        builder.HasIndex(l => l.ClientMailboxId);
        builder.Property(l => l.Action).HasConversion<string>().HasMaxLength(32);
        builder.Property(l => l.Details).HasMaxLength(1024);
    }
}

/// <summary>
/// Jobul adreselor operaționale. La fiecare jumătate de minut: pune în coadă dosarele ajunse la
/// „ARR &amp; Cont Flotă” și creează ce e în coadă (inclusiv ce a cerut adminul cu „Creează” sau
/// „Reîncearcă”). O dată pe zi, la 08:00 ora României, citește consumul domeniului.
/// </summary>
internal sealed partial class ClientMailboxJob(IServiceScopeFactory scopeFactory, ILogger<ClientMailboxJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private DateOnly? _usageCheckedOn;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();

                Result<int> processed = await scope.ServiceProvider
                    .GetRequiredService<ICommandHandler<ProcessClientMailboxesCommand, int>>()
                    .Handle(new ProcessClientMailboxesCommand(), stoppingToken);
                if (processed.IsSuccess && processed.Value > 0)
                {
                    LogProcessed(logger, processed.Value);
                }

                if (UsageCheckDue(out DateOnly today))
                {
                    _usageCheckedOn = today;
                    Result<MailboxUsageDto> usage = await scope.ServiceProvider
                        .GetRequiredService<ICommandHandler<CheckMailboxUsageCommand, MailboxUsageDto>>()
                        .Handle(new CheckMailboxUsageCommand(), stoppingToken);
                    if (usage.IsFailure)
                    {
                        LogUsageFailed(logger, usage.Error.Description);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private bool UsageCheckDue(out DateOnly today)
    {
        DateTime romania = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("E. Europe Standard Time"));
        today = DateOnly.FromDateTime(romania);
        return romania.Hour >= 8 && _usageCheckedOn != today;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Emailuri operaționale procesate: {Count}.")]
    private static partial void LogProcessed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumul domeniului de email nu s-a putut citi: {Error}")]
    private static partial void LogUsageFailed(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Eroare în ClientMailboxJob.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

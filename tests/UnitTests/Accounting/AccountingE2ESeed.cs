using System.Globalization;
using System.Text.Json;
using Application.Accounting;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Domain.Banking;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Authentication;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Infrastructure.Security;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using Document = Domain.Documents.Document;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Seed-ul testelor e2e ale contabilității (B9): o variantă mică a fixtures din frontend, pe baza
/// backend-ului pornit pentru e2e. Nu face nimic fără <c>RIDELANCE_SEED_DATABASE</c> (baza e2e,
/// niciodată cea reală) și <c>RIDELANCE_SEED_UPLOADS</c> (<c>FileStorage:BasePath</c> al backend-ului).
/// </summary>
/// <remarks>
/// Luna fiscală e august 2026, ca în fixtures: Ion și Ana ies gata după confirmarea în bloc, Bogdan
/// are o sumă greșită în factura Bolt, lui George îi lipsește factura Uber. Ion are și ledger pe
/// august–octombrie („o zi în RIDElance”, §5.3) și casa de marcat activă.
/// </remarks>
public sealed class AccountingE2ESeed
{
    /// <summary>Contul contabilului de test; parola o citesc și testele Playwright (tests/accounting-e2e).</summary>
    public const string AccountantEmail = "contabil.e2e@ridelance.test";

    public const string AccountantPassword = "E2e-Contabil-2026!";

    private const string Period = "2026-08";

    private static readonly string? Database = Environment.GetEnvironmentVariable("RIDELANCE_SEED_DATABASE");
    private static readonly string? Uploads = Environment.GetEnvironmentVariable("RIDELANCE_SEED_UPLOADS");

    private static readonly Guid Accountant = Guid.NewGuid();
    private int _invoice;

    [Fact]
    public async Task Seed_accounting_fixtures()
    {
        if (string.IsNullOrWhiteSpace(Database) || string.IsNullOrWhiteSpace(Uploads))
        {
            return;
        }

        Database.ShouldContain("e2e", Case.Insensitive, "Seed-ul rulează doar pe o bază e2e.");
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Database).UseSnakeCaseNamingConvention().Options,
            new Events());
        if (await db.Users.AnyAsync(u => u.Email == AccountantEmail))
        {
            return;
        }

        IOptions<EncryptionSettings> encryption = Options.Create(new EncryptionSettings { Key = EncryptionKey() });
        var files = new FileEncryptionService(encryption, Options.Create(new FileStorageSettings { BasePath = Uploads }));
        var secrets = new SecretProtector(encryption);

        db.Users.Add(new User
        {
            Id = Accountant,
            Email = AccountantEmail,
            FirstName = "Contabil",
            LastName = "E2E",
            Role = UserRole.Contabil,
            PasswordHash = new PasswordHasher().Hash(AccountantPassword),
            EmailVerifiedAtUtc = DateTime.UtcNow,
        });

        // Ca în fixtures: certificatele de rezidență valabile pe 2026, iar cota Uber confirmată cu 0%,
        // etichetată explicit (Decizii pct. 2). Seed-ul din migrații le lasă, corect, necompletate.
        foreach (SupplierTaxProfile supplier in await db.SupplierTaxProfiles.ToListAsync())
        {
            supplier.ResidenceCertValidFrom ??= new DateOnly(2026, 1, 1);
            supplier.ResidenceCertValidTo ??= new DateOnly(2026, 12, 31);
            if (supplier.SupplierName.StartsWith("Uber", StringComparison.Ordinal))
            {
                supplier.D100Rate = 0;
                supplier.D100RateConfirmed = true;
                supplier.Note = "fixture – de înlocuit";
            }
        }

        Guid ion = Pfa(db, secrets, "Ion", "Popescu", "12345674");
        Guid ana = Pfa(db, secrets, "Ana", "Georgescu", Cui("4100007"));
        Guid bogdan = Pfa(db, secrets, "Bogdan", "Matei", Cui("4100008"));
        Guid george = Pfa(db, secrets, "George", "Stan", Cui("4100009"));

        await Invoice(db, files, ion, Platform.Bolt, 1000m, 1000m);
        await Report(db, files, ion, Platform.Bolt, 8000m, 1000m);
        await Invoice(db, files, ion, Platform.Uber, 600m, 600m);
        await Report(db, files, ion, Platform.Uber, 5000m, 600m);

        await Invoice(db, files, ana, Platform.Bolt, 900m, 900m);
        await Report(db, files, ana, Platform.Bolt, 6000m, 900m);
        await Invoice(db, files, ana, Platform.Uber, 500m, 500m);
        await Report(db, files, ana, Platform.Uber, 3500m, 500m);

        // Citită greșit: 1.284,50 în loc de 1.248,50 (suma din PDF).
        await Invoice(db, files, bogdan, Platform.Bolt, 1284.50m, 1248.50m);
        await Report(db, files, bogdan, Platform.Bolt, 7900m, 1248.50m);
        await Invoice(db, files, bogdan, Platform.Uber, 714m, 714m);
        await Report(db, files, bogdan, Platform.Uber, 4200m, 714m);

        await Invoice(db, files, george, Platform.Bolt, 1065m, 1065m);
        await Report(db, files, george, Platform.Bolt, 7100m, 1065m);
        await Report(db, files, george, Platform.Uber, 3900m, 663m);

        await IonLedgerAsync(db, files, ion);
        await db.SaveChangesAsync();

        LedgerImportResult bank = (await new RunLedgerImportCommandHandler(db, [new BankLedgerSource(db)], Options.Create(new AccountingOptions()))
            .Handle(new RunLedgerImportCommand(ion), CancellationToken.None)).Value.Single();
        bank.Created.ShouldBeGreaterThan(0);
    }

    private Guid Pfa(ApplicationDbContext db, SecretProtector secrets, string firstName, string lastName, string cui)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{firstName}.{lastName}.e2e@ridelance.test",
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = "-",
            EmailVerifiedAtUtc = DateTime.UtcNow,
        };
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            FullName = $"{firstName} {lastName}",
            LegalName = $"{lastName.ToUpperInvariant()} {firstName.ToUpperInvariant()} PFA",
            Cui = cui,
            Street = "Str. Exemplu",
            Number = "1",
            City = "București",
            County = "București",
            OnboardingCompletedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            // Portofoliul contabilului de test: „Clienți PFA” și spațiul de lucru îi arată doar clienții alocați.
            AssignedContabilId = Accountant,
        };
        db.Users.Add(user);
        db.PfaRegistrations.Add(pfa);
        db.PfaBankAccountDeclarations.Add(new PfaBankAccountDeclaration
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            BankName = "Banca Transilvania",
            IbanEncrypted = secrets.Protect("RO49AAAA1B31007593840000"),
            IbanMasked = "••••0000",
        });
        db.PfaAccountingEngagements.Add(new PfaAccountingEngagement { Id = Guid.NewGuid(), PfaRegistrationId = pfa.Id, StartDate = new DateOnly(2026, 1, 1), Status = EngagementStatus.Active });
        db.PfaAccountingSettings.AddRange(
            Setting(pfa.Id, PfaAccountingSettingKeys.Art317, "true", new DateOnly(2025, 9, 1), "Cod special de TVA primit"),
            Setting(pfa.Id, PfaAccountingSettingKeys.Art317VatCode, $"\"RO{cui}\"", new DateOnly(2025, 9, 1), "Codul din certificatul de înregistrare în scopuri de TVA"),
            Setting(pfa.Id, PfaAccountingSettingKeys.Platforms, "[\"BOLT\",\"UBER\"]", new DateOnly(2026, 1, 1), "Platformele din onboarding"),
            Setting(pfa.Id, PfaAccountingSettingKeys.VehicleDeductibility, "\"100_PERCENT\"", new DateOnly(2026, 1, 1), "Autoturism folosit exclusiv pentru curse"));
        return pfa.Id;
    }

    private PfaAccountingSetting Setting(Guid pfaId, string key, string value, DateOnly from, string note) => new()
    {
        Id = Guid.NewGuid(), PfaRegistrationId = pfaId, Key = key, ValueJson = value, ValidFrom = from, Note = note, ChangedByUserId = Accountant,
    };

    /// <summary>Factura de comision; <paramref name="inPdf"/> e suma tipărită, <paramref name="read"/> cea citită.</summary>
    private async Task Invoice(ApplicationDbContext db, FileEncryptionService files, Guid pfaId, Platform platform, decimal read, decimal inPdf)
    {
        bool bolt = platform == Platform.Bolt;
        string number = $"{(bolt ? "EE-BOLT" : "UBR-RO")}-2026-08-{1000 + ++_invoice}";
        string[] lines =
        [
            bolt ? "Bolt Operations OÜ, Vana-Lõuna 15, Tallinn, Estonia" : "Uber B.V., Burgerweeshuispad 301, Amsterdam",
            $"VAT: {(bolt ? "EE102090374" : "NL852071589B01")}",
            $"Factura {number} din 31.08.2026",
            "Perioada: 01.08.2026 – 31.08.2026",
            $"Comision platformă: {AccountingJson.Amount(inPdf)} RON",
        ];
        await AddAsync(db, files, pfaId, platform, PlatformDocumentType.CommissionInvoice, $"factura-{number}.pdf", lines, new DocumentExtraction
        {
            SupplierName = bolt ? "Bolt Operations OÜ" : "Uber B.V.",
            SupplierCountry = bolt ? "EE" : "NL",
            SupplierVatId = bolt ? "EE102090374" : "NL852071589B01",
            InvoiceNumber = number,
            InvoiceDate = new DateOnly(2026, 8, 31),
            PeriodFrom = new DateOnly(2026, 8, 1),
            PeriodTo = new DateOnly(2026, 8, 31),
            Currency = "RON",
            Amount = read,
            CommissionAmount = read,
        });
    }

    private static async Task Report(ApplicationDbContext db, FileEncryptionService files, Guid pfaId, Platform platform, decimal income, decimal commission)
    {
        string name = platform == Platform.Bolt ? "Bolt" : "Uber";
        string[] lines =
        [
            $"Raport lunar {name} — august 2026",
            "Perioada: 01.08.2026 – 31.08.2026",
            $"Venit brut din curse: {AccountingJson.Amount(income)} RON",
            $"Comision: {AccountingJson.Amount(commission)} RON",
        ];
        await AddAsync(db, files, pfaId, platform, PlatformDocumentType.PlatformReport, $"raport-{(platform == Platform.Bolt ? "bolt" : "uber")}-2026-08.pdf", lines, new DocumentExtraction
        {
            SupplierName = platform == Platform.Bolt ? "Bolt Operations OÜ" : "Uber B.V.",
            InvoiceDate = new DateOnly(2026, 9, 1),
            PeriodFrom = new DateOnly(2026, 8, 1),
            PeriodTo = new DateOnly(2026, 8, 31),
            Currency = "RON",
            Amount = income,
            CommissionAmount = commission,
        });
    }

    /// <summary>
    /// Documentul, deja citit: procesarea lunii îl duce în <c>PENDING_CONFIRMATION</c> sau
    /// <c>NEEDS_REVIEW</c>, fără apel la modelul AI.
    /// </summary>
    private static async Task AddAsync(
        ApplicationDbContext db,
        FileEncryptionService files,
        Guid pfaId,
        Platform platform,
        PlatformDocumentType type,
        string fileName,
        string[] lines,
        DocumentExtraction extraction)
    {
        byte[] pdf = Pdf(lines);
        Document file = await StoreAsync(db, files, pfaId, fileName, pdf);
        string text = string.Join('\n', lines);
        bool amountsInText = new[] { extraction.Amount, extraction.CommissionAmount }
            .All(amount => amount is null || text.Contains(AccountingJson.Amount(amount.Value), StringComparison.Ordinal));
        var document = new PlatformDocument
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaId,
            Period = Period,
            Platform = platform,
            DocumentType = type,
            SourceDocumentId = file.Id,
            FileHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(pdf)),
            Status = amountsInText ? PlatformDocumentStatus.PendingConfirmation : PlatformDocumentStatus.NeedsReview,
            PdfText = text,
            HasTextLayer = true,
            UploadedByUserId = Accountant,
            UploadedAtUtc = DateTime.UtcNow,
        };
        extraction.Id = Guid.NewGuid();
        extraction.PlatformDocumentId = document.Id;
        extraction.Version = 1;
        extraction.IsCurrent = true;
        extraction.ModelId = "seed-e2e";
        extraction.PromptVersion = "seed";
        extraction.ChecksResultJson = amountsInText
            ? "[]"
            : AccountingJson.Serialize(new[] { new Application.Accounting.Contracts.DocumentCheck(
                DocumentCheckCode.AmountInText, false,
                $"Suma {AccountingJson.Amount(extraction.CommissionAmount ?? 0)} (comision) nu apare în textul documentului.", null) });
        db.PlatformDocuments.Add(document);
        db.DocumentExtractions.Add(extraction);
    }

    /// <summary>Ion: bancă august–octombrie (payout-uri, OMV, service), casa de marcat activă din septembrie.</summary>
    private static async Task IonLedgerAsync(ApplicationDbContext db, FileEncryptionService files, Guid ion)
    {
        PfaRegistration pfa = db.PfaRegistrations.Local.Single(p => p.Id == ion);
        var connection = new BankConnection
        {
            Id = Guid.NewGuid(), UserId = pfa.UserId, Provider = "e2e", InstitutionId = "BTRLRO22", InstitutionName = "Banca Transilvania",
            Status = BankConnectionStatus.Linked, CreatedAtUtc = DateTime.UtcNow,
        };
        var account = new BankAccount { Id = Guid.NewGuid(), BankConnectionId = connection.Id, UserId = pfa.UserId, ProviderAccountId = "e2e-ron", Currency = "RON", IsActive = true };
        db.BankConnections.Add(connection);
        db.BankAccounts.Add(account);
        db.PfaBankAccountDeclarations.Local.Single(d => d.PfaRegistrationId == ion).BankConnectionId = connection.Id;

        (string Date, decimal Amount, string Counterparty, string Details)[] transactions =
        [
            ("2026-08-05", 1650m, "BOLT OPERATIONS OU", "Payout saptamana 31"),
            ("2026-08-12", 1820m, "BOLT OPERATIONS OU", "Payout saptamana 32"),
            ("2026-08-14", -280m, "OMV PETROM SA", "Plata card OMV Petrom"),
            ("2026-08-20", -1350m, "DATECS", "Casa de marcat Datecs DP-25X"),
            ("2026-09-10", -1000m, "AUTO SERVICE SRL", "Revizie"),
            ("2026-10-10", -300m, "OMV PETROM SA", "Plata card OMV Petrom Brasov"),
            ("2026-10-10", 1850m, "BOLT OPERATIONS OU", "Payout saptamana 40"),
        ];
        foreach ((string date, decimal amount, string counterparty, string details) in transactions)
        {
            db.BankTransactions.Add(new BankTransaction
            {
                Id = Guid.NewGuid(),
                BankAccountId = account.Id,
                UserId = pfa.UserId,
                ProviderTransactionId = Guid.NewGuid().ToString("N"),
                BookingDate = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Amount = amount,
                Currency = "RON",
                CounterpartyName = counterparty,
                RemittanceInfo = details,
                ImportedAtUtc = DateTime.UtcNow,
            });
        }

        Document evidence = await StoreAsync(db, files, ion, "dovada-fiscalizare.pdf", Pdf(["Dovada de fiscalizare — casa de marcat Datecs DP-25X"]));
        db.CashRegisterStates.Add(new CashRegisterState
        {
            PfaRegistrationId = ion, CashRequested = true, CashEnabled = true, Status = CashRegisterStatus.Active,
            ActivationDate = new DateOnly(2026, 9, 1), EvidenceDocumentId = evidence.Id, CashRequestedAnsweredAtUtc = DateTime.UtcNow,
        });
    }

    private static async Task<Document> StoreAsync(ApplicationDbContext db, FileEncryptionService files, Guid pfaId, string fileName, byte[] content)
    {
        Guid userId = db.PfaRegistrations.Local.Single(p => p.Id == pfaId).UserId;
        string stored = $"{Guid.NewGuid()}.pdf";
        Application.Abstractions.Services.EncryptedFileResult encrypted;
        using (var stream = new MemoryStream(content))
        {
            encrypted = await files.EncryptAndSaveAsync(stream, stored, CancellationToken.None);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PfaRegistrationId = pfaId,
            OriginalFileName = fileName,
            StoredFileName = stored,
            ContentType = "application/pdf",
            Category = DocumentCategory.Other,
            Status = DocumentStatus.Verified,
            Origin = DocumentOrigin.AccountingUpload,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = content.Length,
            UploadedAtUtc = DateTime.UtcNow,
        };
        db.Documents.Add(document);
        return document;
    }

    private static byte[] Pdf(string[] lines) =>
        QuestPDF.Fluent.Document.Create(container => container.Page(page =>
        {
            page.Margin(40);
            page.Content().Column(column =>
            {
                foreach (string line in lines)
                {
                    column.Item().Text(line).FontSize(12);
                }
            });
        })).GeneratePdf();

    /// <summary>CUI valid (cifra de control cu cheia 753217532) pentru o bază de 7 cifre.</summary>
    private static string Cui(string digits)
    {
        const string key = "753217532";
        string padded = digits.PadLeft(9, '0');
        int sum = padded.Select((c, index) => (c - '0') * (key[index] - '0')).Sum();
        int control = sum * 10 % 11 % 10;
        return digits + control.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Cheia backend-ului e2e: aceeași ca în appsettings.Development.json, dacă nu e dată altfel.</summary>
    private static string EncryptionKey()
    {
        if (Environment.GetEnvironmentVariable("RIDELANCE_SEED_ENCRYPTION_KEY") is { Length: > 0 } key)
        {
            return key;
        }

        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "src", "Web.Api", "appsettings.Development.json")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!, "src", "Web.Api", "appsettings.Development.json")));
        return settings.RootElement.GetProperty("Encryption").GetProperty("Key").GetString()!;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

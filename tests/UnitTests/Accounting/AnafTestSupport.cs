using Application.Abstractions.Anaf;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Domain.Accounting;
using SharedKernel;

namespace UnitTests.Accounting;

/// <summary>Cazul Ion Popescu (spec §5.1) ca intrare de XML, plus dublurile pentru fișiere și validatorul ANAF.</summary>
internal static class AnafTestSupport
{
    public const string Period = "2026-08";

    /// <summary>Codul TVA al Uber B.V., cum apare pe facturile reale (trece și algoritmul NL al validatorului ANAF).</summary>
    public const string UberVatId = "NL852071589B01";

    public static readonly AnafTaxpayer Ion = new(
        "12345674",
        "POPESCU ION PFA",
        "Str. Exemplu 1, București",
        "Popescu",
        "Ion",
        "TITULAR",
        "Banca Transilvania",
        "RO49AAAA1B31007593840000",
        "RO12345674");

    public static AnafDeclarationInput Input(DeclarationType type) => type switch
    {
        DeclarationType.D100 => new(type, Period, false, Ion,
        [
            Line("EE-BOLT-2026-08-1000", 1000m, 20m, "Bolt Operations OÜ", "EE", "EE102090374"),
            Line("UBR-RO-2026-08-1002", 600m, 0m, "Uber B.V.", "NL", UberVatId),
        ], 20m, "634", "5503XXXXXX", Due),
        DeclarationType.D301 => new(type, Period, false, Ion,
        [
            Line("EE-BOLT-2026-08-1000", 1000m, 210m, "Bolt Operations OÜ", "EE", "EE102090374"),
            Line("UBR-RO-2026-08-1002", 600m, 126m, "Uber B.V.", "NL", UberVatId),
        ], 336m, "301", null, Due),
        _ => new(type, Period, false, Ion,
        [
            Line(null, 1000m, 0m, "Bolt Operations OÜ", "EE", "EE102090374"),
            Line(null, 600m, 0m, "Uber B.V.", "NL", UberVatId),
        ], 0m, "390", null, Due),
    };

    /// <summary>Termenul lunii de test, din regula <c>MONTHLY:25</c>.</summary>
    public static readonly DateOnly Due = new(2026, 9, 25);

    private static AnafDeclarationLine Line(string? number, decimal @base, decimal value, string supplier, string country, string vatId) =>
        new(number, number is null ? null : new DateOnly(2026, 8, 31), @base, "RON", null, @base, value, supplier, country, vatId);
}

internal sealed class MemoryFiles : IFileEncryptionService
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public async Task<EncryptedFileResult> EncryptAndSaveAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);
        Files[fileName] = buffer.ToArray();
        return new EncryptedFileResult(fileName, "iv");
    }

    public Task<Stream> DecryptAndReadAsync(string encryptedFilePath, string iv, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(Files[encryptedFilePath]));
}

internal sealed class PlainSecrets : ISecretProtector
{
    public string Protect(string plainText) => plainText;

    public string Unprotect(string protectedText) => protectedText;
}

/// <summary>Împuternicitul RIDElance și împuternicirea semnată a unui client, pentru testele D700.</summary>
internal static class AnafMandateTestData
{
    public const string Number = "ANAF-000123";
    public const string FileName = "Imputernicire_ANAF-000123.pdf";
    public static readonly byte[] Pdf = "%PDF-1.4 imputernicire"u8.ToArray();
    public static readonly DateTime IssuedAtUtc = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    public static Microsoft.Extensions.Options.IOptions<Application.PfaRegistrations.Onboarding.AnafMandate.AnafMandatarOptions> Mandatar() =>
        Microsoft.Extensions.Options.Options.Create(new Application.PfaRegistrations.Onboarding.AnafMandate.AnafMandatarOptions
        {
            FullName = "Maria Ionescu",
            LastName = "Ionescu",
            FirstName = "Maria",
            Cnp = "2800101410015",
            Domicile = "Str. Exemplu nr. 1, Bucuresti, sector 1",
            IdSeries = "RX",
            IdNumber = "123456",
            Email = "maria@ridelance.ro",
            Phone = "0722 123 456",
        });

    /// <summary>Pachetul de semnături cu împuternicirea ANAF generată și, implicit, semnată.</summary>
    public static async Task<Guid> SeedAsync(
        Infrastructure.Database.ApplicationDbContext db, IFileEncryptionService files, Guid pfaId, Guid userId, bool signed = true)
    {
        using var stream = new MemoryStream(Pdf);
        EncryptedFileResult stored = await files.EncryptAndSaveAsync(stream, $"{Guid.NewGuid()}.pdf", CancellationToken.None);
        var document = new Domain.Documents.Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PfaRegistrationId = pfaId,
            OriginalFileName = FileName,
            StoredFileName = stored.FilePath,
            ContentType = "application/pdf",
            Category = Domain.Documents.DocumentCategory.ImputernicireAnaf,
            EncryptedFilePath = stored.FilePath,
            EncryptionIv = stored.Iv,
            FileSize = Pdf.Length,
            UploadedAtUtc = IssuedAtUtc,
            IssuedAtUtc = IssuedAtUtc,
        };
        var packet = new Domain.PfaRegistrations.OnboardingSignaturePacket { Id = Guid.NewGuid(), PfaRegistrationId = pfaId };
        db.Documents.Add(document);
        db.OnboardingSignaturePackets.Add(packet);
        db.OnboardingSignatureDocuments.Add(new Domain.PfaRegistrations.OnboardingSignatureDocument
        {
            Id = Guid.NewGuid(),
            PacketId = packet.Id,
            Packet = packet,
            Type = Domain.PfaRegistrations.SignatureDocumentType.PowerOfAttorneyAnaf,
            Label = Number,
            DocumentId = document.Id,
            IsSigned = signed,
            SignedAtUtc = signed ? IssuedAtUtc.AddHours(2) : null,
        });
        await db.SaveChangesAsync();
        return document.Id;
    }
}

/// <summary>Validatorul ANAF simulat: răspunsul se alege per test; cererile se păstrează.</summary>
internal sealed class FakeAnafValidator : IAnafValidatorClient
{
    public static readonly byte[] Pdf = "%PDF-1.4 declaratie"u8.ToArray();

    public Func<DeclarationType, byte[], Result<AnafValidatorResult>> Respond { get; set; } =
        (_, _) => new AnafValidatorResult(true, [], [], "ok", Pdf, 1500, "test");

    public List<(DeclarationType Type, string Version, byte[] Xml)> Calls { get; } = [];

    /// <summary>Declarațiile din afara celor lunare (D700).</summary>
    public Func<string, byte[], Result<AnafValidatorResult>> RespondOther { get; set; } =
        (_, _) => new AnafValidatorResult(true, [], [], "ok", Pdf, 1500, "test");

    public List<(string Type, string Version, byte[] Xml)> OtherCalls { get; } = [];

    /// <summary>Arhivele atașate, în ordinea cererilor care au avut una.</summary>
    public List<byte[]> Attachments { get; } = [];

    public Task<Result<AnafValidatorResult>> ValidateWithAttachmentAsync(
        string declarationType, string validatorVersion, byte[] xml, byte[] attachmentZip, string correlationId, CancellationToken cancellationToken)
    {
        Attachments.Add(attachmentZip);
        return ValidateAsync(declarationType, validatorVersion, xml, correlationId, cancellationToken);
    }

    public Task<Result<AnafValidatorResult>> ValidateAsync(
        string declarationType, string validatorVersion, byte[] xml, string correlationId, CancellationToken cancellationToken)
    {
        if (Enum.TryParse(declarationType, out DeclarationType type))
        {
            Calls.Add((type, validatorVersion, xml));
            return Task.FromResult(Respond(type, xml));
        }

        OtherCalls.Add((declarationType, validatorVersion, xml));
        return Task.FromResult(RespondOther(declarationType, xml));
    }
}

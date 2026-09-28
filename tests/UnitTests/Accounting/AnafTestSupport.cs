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
        ], 20m),
        DeclarationType.D301 => new(type, Period, false, Ion,
        [
            Line("EE-BOLT-2026-08-1000", 1000m, 210m, "Bolt Operations OÜ", "EE", "EE102090374"),
            Line("UBR-RO-2026-08-1002", 600m, 126m, "Uber B.V.", "NL", UberVatId),
        ], 336m),
        _ => new(type, Period, false, Ion,
        [
            Line(null, 1000m, 0m, "Bolt Operations OÜ", "EE", "EE102090374"),
            Line(null, 600m, 0m, "Uber B.V.", "NL", UberVatId),
        ], 0m),
    };

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

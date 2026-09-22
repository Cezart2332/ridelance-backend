using System.Security.Cryptography;
using Application.Abstractions.Services;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace UnitTests.Documents;

/// <summary>
/// Stocarea criptată: două fișiere cu același nume citibil (specimenul de semnătură al două firme,
/// două poze „IMG_1234.jpg”) nu au voie să ajungă pe aceeași cale. Suprascrierea lăsa primul
/// document cu un IV care nu se mai potrivea cu conținutul, iar decriptarea lui crăpa cu
/// „The computed authentication tag did not match the input authentication tag”.
/// </summary>
public sealed class FileEncryptionServiceTests : IDisposable
{
    private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"ridelance-enc-{Guid.NewGuid():N}");

    [Fact]
    public async Task Acelasi_nume_de_fisier_nu_suprascrie_documentul_anterior()
    {
        FileEncryptionService service = NewService();
        byte[] first = [1, 2, 3, 4];
        byte[] second = [9, 8, 7];

        EncryptedFileResult a = await service.EncryptAndSaveAsync(new MemoryStream(first), "specimen-semnatura.png", default);
        EncryptedFileResult b = await service.EncryptAndSaveAsync(new MemoryStream(second), "specimen-semnatura.png", default);

        a.FilePath.ShouldNotBe(b.FilePath);
        (await ReadAsync(service, a)).ShouldBe(first);
        (await ReadAsync(service, b)).ShouldBe(second);
    }

    [Fact]
    public async Task Continutul_se_decripteaza_doar_cu_IV_ul_lui()
    {
        FileEncryptionService service = NewService();
        EncryptedFileResult saved = await service.EncryptAndSaveAsync(new MemoryStream([1, 2, 3]), "doc.pdf", default);
        string otherIv = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));

        await Should.ThrowAsync<AuthenticationTagMismatchException>(
            () => service.DecryptAndReadAsync(saved.FilePath, otherIv, default));
    }

    [Fact]
    public void Numele_de_pe_disc_pastreaza_originalul_dar_ramane_unic()
    {
        string name = FileEncryptionService.UniqueStoredName("specimen semnatura.png");

        name.ShouldStartWith("specimen semnatura-");
        name.ShouldEndWith(".png.enc");
        name.ShouldNotBe(FileEncryptionService.UniqueStoredName("specimen semnatura.png"));
        // Caracterele interzise pe disc nu ajung într-o cale.
        // Separatorii de cale și caracterele interzise nu ajung într-un nume de fișier.
        FileEncryptionService.UniqueStoredName("dir/b:c*.png").ShouldStartWith("b_c_-");
    }

    private FileEncryptionService NewService() => new(
        Options.Create(new EncryptionSettings { Key = "cheie-de-test" }),
        Options.Create(new FileStorageSettings { BasePath = _basePath }));

    private static async Task<byte[]> ReadAsync(FileEncryptionService service, EncryptedFileResult file)
    {
        using Stream stream = await service.DecryptAndReadAsync(file.FilePath, file.Iv, default);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }
}

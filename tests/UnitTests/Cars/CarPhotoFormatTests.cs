using Application.Cars;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Tipul pozei se citește din conținut. Cazurile reale din producție: un „.WEBP” cu JPEG înăuntru
/// (ieșit din blurare) și un WebP trimis de telefon cu numele „.JPG”.
/// </summary>
public sealed class CarPhotoFormatTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static readonly byte[] Webp = [.."RIFF"u8, 0xE0, 0xB8, 0, 0, .."WEBPVP8 "u8];

    [Fact]
    public void ReadsTheRealFormat()
    {
        CarPhotoFormat.ExtensionOf(Jpeg).ShouldBe(".jpg");
        CarPhotoFormat.ExtensionOf(Png).ShouldBe(".png");
        CarPhotoFormat.ExtensionOf(Webp).ShouldBe(".webp");
        CarPhotoFormat.ExtensionOf([1, 2, 3]).ShouldBeNull();
    }

    [Theory]
    [InlineData("a.WEBP", false)]
    [InlineData("a.JPEG", true)]
    [InlineData("a.jpg", true)]
    [InlineData("a.png", false)]
    public void JpegContentMatchesOnlyJpegExtensions(string fileName, bool matches)
    {
        CarPhotoFormat.ExtensionMatches(fileName, Jpeg).ShouldBe(matches);
    }

    [Fact]
    public void WebpNamedJpgDoesNotMatch()
    {
        CarPhotoFormat.ExtensionMatches("a.JPG", Webp).ShouldBeFalse();
        CarPhotoFormat.ExtensionMatches("a.webp", Webp).ShouldBeTrue();
    }
}

using Infrastructure.Dossiers;
using Shouldly;
using Xunit;

namespace ArchitectureTests.Dossiers;

/// <summary>
/// Ce citim din antetul unei poze înainte s-o punem în dosar.
///
/// Miza e orientarea. Un telefon ținut pe orizontală scrie pixelii tot în portret și lasă în EXIF
/// indicația „rotește la afișare". Galeriile o respectă, PDF-ul nu are așa ceva — deci un act
/// fotografiat perfect ajungea culcat în dosarul depus la ghișeu, pe o pagină portret, mic și
/// înconjurat de alb.
/// </summary>
public sealed class ImageInfoTests
{
    [Fact]
    public void JpegWithoutExif_IsReadAsUnrotated()
    {
        var info = ImageInfo.Read(Jpeg(2340, 1654));

        info.Width.ShouldBe(2340);
        info.Height.ShouldBe(1654);
        info.Orientation.ShouldBe(1);
        info.IsLandscape.ShouldBeTrue();
    }

    [Theory]
    // Cele două valori pe care le scriu telefoanele când sunt ținute pe orizontală.
    [InlineData(6)]
    [InlineData(8)]
    public void JpegRotatedByTheCamera_SwapsTheSidesForDisplay(int orientation)
    {
        // Pixelii sunt în portret, dar actul e fotografiat pe lat: la afișare e landscape.
        var info = ImageInfo.Read(Jpeg(1654, 2340, orientation));

        info.Orientation.ShouldBe(orientation);
        info.QuarterTurn.ShouldBeTrue();
        info.DisplayWidth.ShouldBe(2340);
        info.DisplayHeight.ShouldBe(1654);
        info.IsLandscape.ShouldBeTrue();
    }

    [Fact]
    public void UpsideDownJpeg_KeepsItsSides()
    {
        var info = ImageInfo.Read(Jpeg(1654, 2340, orientation: 3));

        info.QuarterTurn.ShouldBeFalse();
        info.IsLandscape.ShouldBeFalse();
    }

    [Fact]
    public void Png_IsReadFromItsHeader()
    {
        var info = ImageInfo.Read(Png(800, 600));

        info.Width.ShouldBe(800);
        info.Height.ShouldBe(600);
        info.IsLandscape.ShouldBeTrue();
    }

    [Fact]
    public void Garbage_IsTreatedAsPortraitInsteadOfThrowing()
    {
        // Un antet ciudat nu are voie să pice generarea dosarului: iese ca înainte, nu în cap.
        var info = ImageInfo.Read([0xFF, 0xD8, 0xFF, 0x01, 0x02]);

        info.Orientation.ShouldBe(1);
        info.IsLandscape.ShouldBeFalse();
    }

    /// <summary>Un JPEG cât să aibă antet: APP1/EXIF opțional, apoi SOF0 cu dimensiunile.</summary>
    private static byte[] Jpeg(int width, int height, int orientation = 0)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        if (orientation > 0)
        {
            // APP1: „Exif\0\0", antet TIFF big-endian, un IFD cu o singură intrare.
            var exif = new List<byte>();
            exif.AddRange("Exif"u8.ToArray());
            exif.AddRange([0x00, 0x00]);
            exif.AddRange([(byte)'M', (byte)'M', 0x00, 0x2A]);
            exif.AddRange([0x00, 0x00, 0x00, 0x08]);          // primul IFD, imediat după antet
            exif.AddRange([0x00, 0x01]);                       // o intrare
            exif.AddRange([0x01, 0x12]);                       // eticheta Orientation
            exif.AddRange([0x00, 0x03]);                       // tip SHORT
            exif.AddRange([0x00, 0x00, 0x00, 0x01]);           // o valoare
            exif.AddRange([0x00, (byte)orientation, 0x00, 0x00]);
            exif.AddRange([0x00, 0x00, 0x00, 0x00]);           // fără IFD următor

            int length = exif.Count + 2;
            bytes.AddRange([0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF)]);
            bytes.AddRange(exif);
        }

        bytes.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        bytes.AddRange([(byte)(height >> 8), (byte)(height & 0xFF)]);
        bytes.AddRange([(byte)(width >> 8), (byte)(width & 0xFF)]);
        bytes.AddRange(new byte[12]);

        return [.. bytes];
    }

    private static byte[] Png(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange([0x00, 0x00, 0x00, 0x0D]);
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange([(byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width]);
        bytes.AddRange([(byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height]);
        bytes.AddRange(new byte[8]);

        return [.. bytes];
    }
}

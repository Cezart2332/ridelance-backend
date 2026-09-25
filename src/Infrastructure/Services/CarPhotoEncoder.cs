using OpenCvSharp;

namespace Infrastructure.Services;

/// <summary>
/// Forma în care se salvează orice poză de mașină: JPEG, cel mult 2048 px pe latura lungă.
///
/// Pozele veneau direct din camera telefonului — 5712×4284, 6 MB. Pe date mobile lista de mașini
/// încărca zeci de megabytes, iar unele poze pur și simplu nu mai apăreau. 2048 px ajung și pentru
/// galeria pe tot ecranul.
/// </summary>
internal static class CarPhotoEncoder
{
    public const int MaxSide = 2048;
    private const int JpegQuality = 85;

    public static byte[] ToWebJpeg(Mat image)
    {
        int longSide = Math.Max(image.Width, image.Height);
        if (longSide <= MaxSide)
        {
            return image.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
        }

        double scale = (double)MaxSide / longSide;
        var size = new Size(
            Math.Max(1, (int)Math.Round(image.Width * scale)),
            Math.Max(1, (int)Math.Round(image.Height * scale)));

        using var resized = new Mat();
        Cv2.Resize(image, resized, size, 0, 0, InterpolationFlags.Area);
        return resized.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality));
    }

    /// <summary>Re-encodează o poză deja salvată. Null dacă nu se poate decoda.</summary>
    public static byte[]? TryReencode(byte[] bytes)
    {
        using Mat image = Cv2.ImDecode(bytes, ImreadModes.Color);
        return image.Empty() ? null : ToWebJpeg(image);
    }
}

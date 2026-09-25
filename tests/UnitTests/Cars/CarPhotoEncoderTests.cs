using Application.Cars;
using Infrastructure.Services;
using OpenCvSharp;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Orice poză salvată iese JPEG și cel mult 2048 px pe latura lungă, oricât de mare a venit.
/// </summary>
public sealed class CarPhotoEncoderTests
{
    [Fact]
    public void LargePhotoIsShrunkToJpeg()
    {
        using var big = new Mat(new Size(5712, 4284), MatType.CV_8UC3, new Scalar(40, 120, 200));
        byte[] png = big.ToBytes(".png");

        byte[]? jpeg = CarPhotoEncoder.TryReencode(png);

        jpeg.ShouldNotBeNull();
        CarPhotoFormat.ExtensionOf(jpeg).ShouldBe(".jpg");
        using Mat decoded = Cv2.ImDecode(jpeg, ImreadModes.Color);
        decoded.Width.ShouldBe(2048);
        decoded.Height.ShouldBe(1536);
    }

    [Fact]
    public void SmallPhotoKeepsItsSize()
    {
        using var small = new Mat(new Size(800, 600), MatType.CV_8UC3, new Scalar(10, 10, 10));

        using Mat decoded = Cv2.ImDecode(CarPhotoEncoder.ToWebJpeg(small), ImreadModes.Color);

        decoded.Width.ShouldBe(800);
        decoded.Height.ShouldBe(600);
    }

    [Fact]
    public void GarbageIsNotAPhoto()
    {
        CarPhotoEncoder.TryReencode([1, 2, 3, 4]).ShouldBeNull();
    }
}

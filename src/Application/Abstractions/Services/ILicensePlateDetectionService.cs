namespace Application.Abstractions.Services;

public interface ILicensePlateDetectionService
{
    /// <summary>
    /// Processes an image stream, detects license plates, and returns the processed image with plates blurred.
    /// </summary>
    /// <param name="imageStream">The input image stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A byte array representing the processed image (JPEG format).</returns>
    Task<byte[]> ProcessImageAsync(Stream imageStream, CancellationToken cancellationToken = default);
}

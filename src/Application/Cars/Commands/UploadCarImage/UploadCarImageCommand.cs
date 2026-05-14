using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.UploadCarImage;

public sealed record UploadCarImageCommand(Guid CarId, string FileName, Stream FileStream, string ContentType) : ICommand<Guid>;

internal sealed class UploadCarImageCommandHandler(
    IApplicationDbContext context,
    ILicensePlateDetectionService licensePlateDetectionService)
    : ICommandHandler<UploadCarImageCommand, Guid>
{
    private static readonly string[] AllowedTypes = ["IMAGE/JPEG", "IMAGE/JPG", "IMAGE/PNG", "IMAGE/WEBP"];
    private const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    public async Task<Result<Guid>> Handle(UploadCarImageCommand command, CancellationToken cancellationToken)
    {
        string contentTypeUpper = command.ContentType.ToUpperInvariant();

        if (!AllowedTypes.Contains(contentTypeUpper))
        {
            return Result.Failure<Guid>(Error.Problem("CarImage.InvalidType", "Tip fisier invalid. Acceptam JPEG, PNG, WebP."));
        }

        if (command.FileStream.Length > MaxSizeBytes)
        {
            return Result.Failure<Guid>(Error.Problem("CarImage.TooLarge", "Fisierul este prea mare. Maximum 10 MB."));
        }

        Car? car = await context.Cars
            .Include(c => c.Images)
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Car.NotFound", "Masina nu a fost gasita."));
        }

        // Save to disk
        string uploadsDir = Path.Combine("uploads", "cars");
        Directory.CreateDirectory(uploadsDir);

        string extension = Path.GetExtension(command.FileName).ToUpperInvariant();
        string safeFileName = $"{Guid.NewGuid()}{extension}";
        string filePath = Path.Combine(uploadsDir, safeFileName);

        // Automatically detect and blur license plates
        command.FileStream.Seek(0, SeekOrigin.Begin);
        byte[] processedImage = await licensePlateDetectionService.ProcessImageAsync(command.FileStream, cancellationToken);

        await File.WriteAllBytesAsync(filePath, processedImage, cancellationToken);

        int nextOrder = car.Images.Count > 0 ? car.Images.Max(i => i.DisplayOrder) + 1 : 0;

        var image = new CarImage
        {
            Id = Guid.NewGuid(),
            CarId = command.CarId,
            FileName = safeFileName,
            Url = $"/uploads/cars/{safeFileName}",
            DisplayOrder = nextOrder,
            UploadedAtUtc = DateTime.UtcNow
        };

        context.CarImages.Add(image);
        await context.SaveChangesAsync(cancellationToken);

        return image.Id;
    }
}

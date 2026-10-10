using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.PaidExtras;

/// <summary>
/// Blurează numărul de înmatriculare în pozele mașinilor care au plătit opțiunea „număr ascuns” și
/// au încă poze cu numărul la vedere (urcate înainte de plată). Rulat de un job, la câteva zeci de
/// secunde după plată; întoarce câte poze a trecut.
/// </summary>
public sealed record BlurHiddenPlatePhotosCommand : ICommand<int>;

internal sealed class BlurHiddenPlatePhotosCommandHandler(
    IApplicationDbContext context,
    ILicensePlateDetectionService plates)
    : ICommandHandler<BlurHiddenPlatePhotosCommand, int>
{
    private const int BatchSize = 10;

    public async Task<Result<int>> Handle(BlurHiddenPlatePhotosCommand command, CancellationToken cancellationToken)
    {
        List<CarImage> pending = await context.CarImages
            .Where(i => i.Car.PlateHidden && i.PlateBlurredAtUtc == null)
            .OrderBy(i => i.UploadedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        string directory = Path.Combine("uploads", "cars");

        foreach (CarImage image in pending)
        {
            string path = Path.Combine(directory, image.FileName);
            if (File.Exists(path))
            {
                byte[] blurred;
                await using (FileStream original = File.OpenRead(path))
                {
                    blurred = await plates.ProcessImageAsync(original, blurPlates: true, cancellationToken);
                }

                // Nume nou: adresa veche poate fi deja în cache, în browser sau la CDN, cu numărul la vedere.
                string fileName = $"{Guid.NewGuid()}{CarPhotoFormat.ExtensionOf(blurred) ?? Path.GetExtension(image.FileName)}";
                await File.WriteAllBytesAsync(Path.Combine(directory, fileName), blurred, cancellationToken);
                File.Delete(path);

                image.FileName = fileName;
                image.Url = $"/uploads/cars/{fileName}";
            }

            // Fără fișier nu e nimic de blurat; o notăm oricum, ca jobul să nu o reia la nesfârșit.
            image.PlateBlurredAtUtc = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }

        return pending.Count;
    }
}

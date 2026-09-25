using Application.Abstractions.Data;
using Application.Cars;
using Domain.Cars;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.BackgroundJobs;

/// <summary>
/// O singură trecere la pornire peste pozele de mașini salvate înainte ca uploadul să le
/// normalizeze: cele cu extensia nepotrivită conținutului (un „.WEBP” cu JPEG înăuntru) sau mai
/// mari decât <see cref="CarPhotoEncoder.MaxSide"/> sunt re-encodate ca JPEG, cu nume nou.
///
/// Numărul nu se mai caută: poza a trecut deja prin blurare la upload. E idempotentă — o poză
/// reparată e JPEG, „.jpg” și mică, deci la următoarea pornire nu mai e atinsă.
/// </summary>
internal sealed class CarPhotoRepairJob(
    IServiceScopeFactory scopeFactory,
    ILogger<CarPhotoRepairJob> logger)
    : BackgroundService
{
    /// <summary>Sub pragul ăsta o poză cu extensia corectă nu merită nici măcar decodată.</summary>
    private const long SmallEnoughBytes = 700 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pornirea API-ului are prioritate; reparația poate aștepta câteva secunde.
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        try
        {
            await RepairAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Eroare în CarPhotoRepairJob.");
        }
    }

    private async Task RepairAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IApplicationDbContext context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        List<CarImage> images = await context.CarImages.ToListAsync(cancellationToken);
        string directory = Path.Combine("uploads", "cars");
        int repaired = 0;

        foreach (CarImage image in images)
        {
            string path = Path.Combine(directory, image.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            if (bytes.Length <= SmallEnoughBytes && CarPhotoFormat.ExtensionMatches(image.FileName, bytes))
            {
                continue;
            }

            byte[]? jpeg;
            try
            {
                jpeg = CarPhotoEncoder.TryReencode(bytes);
            }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException)
            {
                logger.LogWarning(ex, "OpenCV lipsește; pozele de mașini rămân cum sunt.");
                return;
            }

            if (jpeg is null)
            {
                logger.LogWarning("Poza {File} nu se poate decoda; rămâne cum e.", image.FileName);
                continue;
            }

            // Un JPEG deja mic și cu extensia bună nu câștigă nimic din re-encodare.
            if (jpeg.Length >= bytes.Length && CarPhotoFormat.ExtensionMatches(image.FileName, bytes))
            {
                continue;
            }

            string fileName = $"{Guid.NewGuid()}.jpg";
            await File.WriteAllBytesAsync(Path.Combine(directory, fileName), jpeg, cancellationToken);

            string oldPath = path;
            image.FileName = fileName;
            image.Url = $"/uploads/cars/{fileName}";
            await context.SaveChangesAsync(cancellationToken);

            // Vechiul fișier pleacă abia după ce baza arată spre cel nou.
            File.Delete(oldPath);
            repaired++;
        }

        if (repaired > 0)
        {
            logger.LogInformation("Am reparat {Count} poze de mașini (tip nepotrivit sau prea mari).", repaired);
        }
    }
}

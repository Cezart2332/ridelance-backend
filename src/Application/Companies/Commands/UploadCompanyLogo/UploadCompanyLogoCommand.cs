using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Commands.UploadCompanyLogo;

/// <summary>Înlocuiește logo-ul firmei. Profilul trebuie să existe deja.</summary>
// Întoarce calea relativă a logo-ului, nu un `Uri`: e servită de același API.
#pragma warning disable CA1055
public sealed record UploadCompanyLogoCommand(string FileName, Stream FileStream, string ContentType)
    : ICommand<string>;
#pragma warning restore CA1055

internal sealed class UploadCompanyLogoCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UploadCompanyLogoCommand, string>
{
    /// <summary>
    /// Tipurile acceptate și extensia sub care se salvează fiecare.
    ///
    /// Extensia se ia de aici, nu din numele trimis de client: numele e input neverificat, iar
    /// singurul lucru care ne trebuie din el — formatul — îl știm deja din content type.
    /// SVG-ul e acceptat pentru că un logo vectorial rămâne curat la 28px pe cardul de mașină.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/svg+xml"] = ".svg",
    };

    private const long MaxSizeBytes = 2 * 1024 * 1024;

    public async Task<Result<string>> Handle(UploadCompanyLogoCommand command, CancellationToken cancellationToken)
    {
        if (!AllowedTypes.TryGetValue(command.ContentType, out string? extension))
        {
            return Result.Failure<string>(
                Error.Problem("CompanyLogo.InvalidType", "Format acceptat: PNG, JPG, WEBP sau SVG."));
        }

        if (command.FileStream.Length > MaxSizeBytes)
        {
            return Result.Failure<string>(
                Error.Problem("CompanyLogo.TooLarge", "Fișierul depășește 2 MB."));
        }

        CompanyProfile? profile = await context.CompanyProfiles
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<string>(Error.Problem(
                "CompanyLogo.NoProfile",
                "Salvează întâi datele firmei, apoi încarcă logo-ul."));
        }

        string uploadsDir = Path.Combine("uploads", "companies");
        Directory.CreateDirectory(uploadsDir);

        string safeFileName = $"{Guid.NewGuid()}{extension}";
        string filePath = Path.Combine(uploadsDir, safeFileName);

        command.FileStream.Seek(0, SeekOrigin.Begin);
        await using (FileStream target = File.Create(filePath))
        {
            await command.FileStream.CopyToAsync(target, cancellationToken);
        }

        // Logo-ul vechi se șterge abia după ce cel nou e pe disc: dacă scrierea eșuează,
        // profilul rămâne cu un logo valid, nu cu o referință moartă.
        string? previous = profile.LogoUrl;

        profile.LogoUrl = $"/uploads/companies/{safeFileName}";
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        DeletePreviousLogo(previous);

        return Result.Success(profile.LogoUrl);
    }

    private static void DeletePreviousLogo(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("/uploads/companies/", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            string path = Path.Combine("uploads", "companies", Path.GetFileName(url));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Un fișier orfan pe disc nu merită să rupă salvarea profilului.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem: lipsa drepturilor pe fișierul vechi nu invalidează logo-ul nou.
        }
    }
}

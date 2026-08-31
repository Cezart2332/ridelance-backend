using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Companies.Page;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Commands.CompanyCover;

/// <summary>Înlocuiește fotografia de fundal a mini-site-ului. Profilul trebuie să existe deja.</summary>
// Întoarce calea relativă, nu un `Uri`: e servită de același API, ca logo-ul.
#pragma warning disable CA1055
public sealed record UploadCompanyCoverCommand(string FileName, Stream FileStream, string ContentType)
    : ICommand<string>;
#pragma warning restore CA1055

/// <summary>Scoate fotografia de fundal. Antetul revine la fundalul plin, din culorile firmei.</summary>
public sealed record DeleteCompanyCoverCommand : ICommand;

internal sealed class UploadCompanyCoverCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UploadCompanyCoverCommand, string>
{
    /// <summary>
    /// Tipurile acceptate și extensia sub care se salvează fiecare.
    ///
    /// Fără SVG, spre deosebire de logo: ăsta e un fundal fotografic lat de 1600 de pixeli, nu o
    /// marcă vectorială, iar un SVG încărcat de utilizator care ajunge servit de pe domeniul
    /// nostru e un fișier care poate conține script.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/jpg"] = ".jpg",
        ["image/webp"] = ".webp",
    };

    // Mai mare decât la logo: o fotografie panoramică decentă nu intră în 2 MB.
    private const long MaxSizeBytes = 5 * 1024 * 1024;

    public async Task<Result<string>> Handle(UploadCompanyCoverCommand command, CancellationToken cancellationToken)
    {
        if (!AllowedTypes.TryGetValue(command.ContentType, out string? extension))
        {
            return Result.Failure<string>(
                Error.Problem("CompanyCover.InvalidType", "Format acceptat: PNG, JPG sau WEBP."));
        }

        if (command.FileStream.Length > MaxSizeBytes)
        {
            return Result.Failure<string>(
                Error.Problem("CompanyCover.TooLarge", "Fișierul depășește 5 MB."));
        }

        CompanyProfile? profile = await context.CompanyProfiles
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<string>(Error.Problem(
                "CompanyCover.NoProfile",
                "Salvează întâi datele firmei, apoi încarcă fotografia."));
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

        // Ca la logo: fotografia veche se șterge abia după ce cea nouă e pe disc și salvată.
        string? previous = profile.CoverImageUrl;

        profile.CoverImageUrl = $"/uploads/companies/{safeFileName}";
        profile.UpdatedAtUtc = DateTime.UtcNow;

        // Fotografia e conținut public încărcat liber, deci trece prin aceeași verificare ca textul.
        CompanyPageReview.SubmitForReview(profile);

        await context.SaveChangesAsync(cancellationToken);

        CompanyUploads.DeleteIfOurs(previous);

        return Result.Success(profile.CoverImageUrl);
    }
}

internal sealed class DeleteCompanyCoverCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<DeleteCompanyCoverCommand>
{
    public async Task<Result> Handle(DeleteCompanyCoverCommand command, CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await context.CompanyProfiles
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure(Error.Problem("CompanyCover.NoProfile", "Profilul firmei nu există."));
        }

        string? previous = profile.CoverImageUrl;
        if (previous is null)
        {
            // Ștergerea a ceva ce nu există nu e o eroare: rezultatul cerut e deja obținut.
            return Result.Success();
        }

        profile.CoverImageUrl = null;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        CompanyPageReview.SubmitForReview(profile);
        await context.SaveChangesAsync(cancellationToken);

        CompanyUploads.DeleteIfOurs(previous);

        return Result.Success();
    }
}

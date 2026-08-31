using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Companies.Page;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Commands.UpdateCompanyPage;

/// <summary>
/// Salvează tot ce ține de mini-site: slogan, descriere, culori și conținutul secțiunilor.
/// </summary>
/// <remarks>
/// Descrierea publică se editează de aici, nu din profilul firmei. Profilul e despre identitatea
/// juridică — denumire, CUI, sediu, IBAN; textul de prezentare e despre pagina publică și se scrie
/// acolo unde se și vede rezultatul. Două formulare care salvează același câmp ar fi ajuns, în
/// timp, două texte diferite, în funcție de care s-a salvat ultimul.
/// </remarks>
public sealed record UpdateCompanyPageCommand(
    string? Tagline,
    string? PublicDescription,
    CompanyPageTheme? Theme,
    CompanyPageContent? Content,
    PickupLocationInput? Pickup) : ICommand<CompanyProfileDto>;

/// <summary>Locul de preluare, așa cum vine din editor.</summary>
public sealed record PickupLocationInput(
    string? Address,
    double? Latitude,
    double? Longitude,
    string? Note);

internal sealed class UpdateCompanyPageCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpdateCompanyPageCommand, CompanyProfileDto>
{
    public async Task<Result<CompanyProfileDto>> Handle(
        UpdateCompanyPageCommand command,
        CancellationToken cancellationToken)
    {
        Result<CompanyPageTheme> theme = CompanyPageSanitizer.SanitizeTheme(command.Theme);
        if (theme.IsFailure)
        {
            return Result.Failure<CompanyProfileDto>(theme.Error);
        }

        Result<CompanyPageContent> content = CompanyPageSanitizer.SanitizeContent(command.Content);
        if (content.IsFailure)
        {
            return Result.Failure<CompanyProfileDto>(content.Error);
        }

        CompanyProfile? profile = await context.CompanyProfiles
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CompanyProfileDto>(Error.Problem(
                "CompanyPage.NoProfile",
                "Salvează întâi datele firmei, apoi personalizează pagina."));
        }

        profile.Tagline = CompanyPageSanitizer.CleanText(command.Tagline, CompanyPageSanitizer.MaxTagline);
        profile.PublicDescription =
            CompanyPageSanitizer.CleanText(command.PublicDescription, CompanyPageSanitizer.MaxDescription);
        profile.PageTheme = theme.Value;
        profile.PageContent = content.Value;

        Result pickup = ApplyPickup(profile, command.Pickup);
        if (pickup.IsFailure)
        {
            return Result.Failure<CompanyProfileDto>(pickup.Error);
        }

        profile.UpdatedAtUtc = DateTime.UtcNow;

        // Salvarea e și cererea de verificare: ce s-a scris aici e o ciornă până când o aprobă
        // cineva din administrare. Versiunea deja publicată rămâne live între timp — vezi
        // `CompanyPageReview.SubmitForReview`.
        CompanyPageReview.SubmitForReview(profile);

        // Nimic din pagina firmei nu intră în scorul anunțurilor: scorul citește descrierea
        // *mașinii* și logo-ul proprietarului, nu textul de prezentare al firmei (spec §5.2).
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(CompanyProfileMapper.ToDto(profile));
    }

    /// <summary>
    /// Adresa, pinul și indicația de la „Unde ne găsiți".
    /// </summary>
    /// <remarks>
    /// Coordonatele se salvează doar în pereche. O latitudine fără longitudine n-ar fi un punct pe
    /// hartă, ar fi jumătate de punct — iar harta de pe mini-site ar trebui să decidă singură ce
    /// face cu ea. Mai bine refuzăm perechea incompletă aici.
    /// </remarks>
    internal static Result ApplyPickup(CompanyProfile profile, PickupLocationInput? pickup)
    {
        profile.PickupAddress = CompanyPageSanitizer.CleanText(pickup?.Address, 512);
        profile.PickupNote = CompanyPageSanitizer.CleanText(pickup?.Note, 600);

        double? latitude = pickup?.Latitude;
        double? longitude = pickup?.Longitude;

        if (latitude is null || longitude is null)
        {
            profile.PickupLatitude = null;
            profile.PickupLongitude = null;
            return Result.Success();
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return Result.Failure(Error.Problem(
                "CompanyPage.InvalidPin",
                "Punctul de pe hartă nu e valid. Alege-l din nou."));
        }

        profile.PickupLatitude = latitude;
        profile.PickupLongitude = longitude;
        return Result.Success();
    }
}

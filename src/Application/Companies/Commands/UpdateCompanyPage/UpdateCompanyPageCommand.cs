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
    CompanyPageContent? Content) : ICommand<CompanyProfileDto>;

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
        profile.UpdatedAtUtc = DateTime.UtcNow;

        // Nimic din pagina firmei nu intră în scorul anunțurilor: scorul citește descrierea
        // *mașinii* și logo-ul proprietarului, nu textul de prezentare al firmei (spec §5.2).
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(CompanyProfileMapper.ToDto(profile));
    }
}

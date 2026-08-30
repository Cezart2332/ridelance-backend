using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Companies;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Commands.UpdateCompanyProfile;

/// <summary>
/// Salvează datele de identitate ale firmei. Creează profilul dacă e prima salvare.
/// </summary>
/// <remarks>
/// Descrierea publică nu mai e aici: s-a mutat în <c>UpdateCompanyPageCommand</c>, lângă restul
/// mini-site-ului, unde se editează cu previzualizarea alături. Profilul rămâne despre identitatea
/// juridică — cea care intră în contracte și facturi.
/// </remarks>
public sealed record UpdateCompanyProfileCommand(
    string LegalName,
    string? Cui,
    string? RegCom,
    string? LegalRepresentative,
    string? RegisteredOffice,
    string? Iban,
    string? Phone,
    string? Email,
    string? Website,
    bool ShowPhone,
    bool ShowEmail,
    bool ShowWhatsApp,
    bool ShowLocation) : ICommand<CompanyProfileDto>;

internal sealed class UpdateCompanyProfileCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpdateCompanyProfileCommand, CompanyProfileDto>
{
    public async Task<Result<CompanyProfileDto>> Handle(
        UpdateCompanyProfileCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.LegalName))
        {
            return Result.Failure<CompanyProfileDto>(
                Error.Problem("CompanyProfile.LegalNameRequired", "Denumirea firmei este obligatorie."));
        }

        User? user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<CompanyProfileDto>(
                Error.Problem("CompanyProfile.UserNotFound", "Contul nu a fost găsit."));
        }

        CompanyProfile? profile = await context.CompanyProfiles
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        if (profile is null)
        {
            profile = new CompanyProfile
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                OwnerType = user.Role == UserRole.CarPoster ? OwnerType.Srl : OwnerType.Pfa,
            };

            // Slug-ul se fixează o singură dată, la creare. Redenumirea firmei nu îl mută:
            // linkurile deja distribuite către /{slug} trebuie să continue să funcționeze.
            Result<string> slug = await ResolveSlugAsync(context, command.LegalName, profile.Id, cancellationToken);
            if (slug.IsFailure)
            {
                return Result.Failure<CompanyProfileDto>(slug.Error);
            }

            profile.Slug = slug.Value;
            context.CompanyProfiles.Add(profile);
        }

        profile.LegalName = command.LegalName.Trim();
        profile.Cui = command.Cui?.Trim();
        profile.RegCom = command.RegCom?.Trim();
        profile.LegalRepresentative = command.LegalRepresentative?.Trim();
        profile.RegisteredOffice = command.RegisteredOffice?.Trim();
        // Fără spații: un IBAN se scrie grupat, dar se compară și se tipărește compact.
        profile.Iban = command.Iban?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        profile.Phone = command.Phone?.Trim();
        profile.Email = command.Email?.Trim();
        profile.Website = command.Website?.Trim();
        profile.ShowPhone = command.ShowPhone;
        profile.ShowEmail = command.ShowEmail;
        profile.ShowWhatsApp = command.ShowWhatsApp;
        profile.ShowLocation = command.ShowLocation;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(CompanyProfileMapper.ToDto(profile));
    }

    /// <summary>Slug-ul preferat, sau varianta cu sufix dacă e deja luat de altcineva.</summary>
    private static async Task<Result<string>> ResolveSlugAsync(
        IApplicationDbContext context,
        string legalName,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        string preferred = CompanySlug.Generate(legalName);

        bool taken = await context.CompanyProfiles
            .AsNoTracking()
            .AnyAsync(p => p.Slug == preferred, cancellationToken);

        // Un slug rezervat se tratează ca luat: nu de altă firmă, ci de o pagină a site-ului.
        return Result.Success(taken || CompanySlug.IsReserved(preferred)
            ? CompanySlug.Disambiguate(preferred, profileId)
            : preferred);
    }
}

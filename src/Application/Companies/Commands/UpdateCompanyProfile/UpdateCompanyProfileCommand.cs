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
public sealed record UpdateCompanyProfileCommand(
    string LegalName,
    string? Cui,
    string? RegCom,
    string? LegalRepresentative,
    string? RegisteredOffice,
    string? Phone,
    string? Email,
    string? Website,
    string? PublicDescription,
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
            // linkurile deja distribuite către /f/{slug} trebuie să continue să funcționeze.
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
        profile.Phone = command.Phone?.Trim();
        profile.Email = command.Email?.Trim();
        profile.Website = command.Website?.Trim();
        profile.PublicDescription = command.PublicDescription?.Trim();
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

        return Result.Success(taken ? CompanySlug.Disambiguate(preferred, profileId) : preferred);
    }
}

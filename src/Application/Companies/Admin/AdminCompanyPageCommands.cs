using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Application.Companies.Commands.UpdateCompanyPage;
using Application.Companies.Page;
using Domain.Companies;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Admin;

/// <summary>
/// Verdictul administrării asupra unui mini-site.
/// </summary>
/// <remarks>
/// <paramref name="Decision" /> e <c>approve</c> sau <c>reject</c>. Aprobarea copiază ciorna în
/// versiunea publică; refuzul scoate pagina de pe internet și lasă motivul scris.
///
/// <paramref name="BlockedSections" /> se trimite doar la aprobare — atunci se decide ce anume din
/// pagină pleacă. La refuz nu pleacă nimic, deci lista n-ar avea ce însemna.
/// </remarks>
public sealed record ReviewCompanyPageCommand(
    Guid ProfileId,
    string Decision,
    string? Note,
    IReadOnlyList<string>? BlockedSections) : ICommand<AdminCompanyPageDetail>;

/// <summary>
/// Pornește sau oprește secțiuni, fără să schimbe verdictul.
/// </summary>
/// <remarks>
/// Lista primită e starea finală, nu o diferență: ce lipsește din ea se deblochează. Un endpoint de
/// „adaugă un blocaj" ar fi cerut unul pereche de scos, iar cele două s-ar fi putut contrazice.
/// </remarks>
public sealed record SetCompanyPageSectionsCommand(
    Guid ProfileId,
    IReadOnlyList<string>? BlockedSections,
    string? Note) : ICommand<AdminCompanyPageDetail>;

/// <summary>
/// Rescrie ciorna paginii din administrare.
/// </summary>
/// <remarks>
/// Nu aprobă. E deliberat: cine corectează un text trebuie apoi să se uite la pagina întreagă și să
/// apese explicit „Aprobă". O editare care publica din mers ar fi făcut din fiecare tăiere de
/// virgulă o publicare nevăzută.
///
/// Trece prin același <see cref="CompanyPageSanitizer"/> ca salvarea proprietarului: plafoane,
/// culori valide și text curățat se aplică la fel, indiferent cine scrie.
/// </remarks>
public sealed record EditCompanyPageAsAdminCommand(
    Guid ProfileId,
    string? Tagline,
    string? PublicDescription,
    CompanyPageTheme? Theme,
    CompanyPageContent? Content,
    PickupLocationInput? Pickup) : ICommand<AdminCompanyPageDetail>;

/// <summary>Scoate fotografia de fundal, și din ciornă, și din versiunea publicată.</summary>
/// <remarks>
/// Și din cea publicată, dinadins: o fotografie nepotrivită trebuie să dispară de pe internet acum,
/// nu la următoarea aprobare. Fișierul de pe disc rămâne — îl șterge proprietarul dacă o înlocuiește
/// — fiindcă ștergerea lui aici ar rupe orice altă referință care ar mai apărea către el.
/// </remarks>
public sealed record RemoveCompanyPageCoverCommand(Guid ProfileId) : ICommand<AdminCompanyPageDetail>;

/// <summary>
/// Scoate logo-ul firmei.
/// </summary>
/// <remarks>
/// Nu trece prin aprobare, spre deosebire de restul paginii, fiindcă logo-ul nu e doar al
/// mini-site-ului: apare pe fiecare card de anunț din marketplace. O verificare care l-ar fi
/// ascuns până la aprobare ar fi golit cardurile tuturor firmelor peste noapte.
///
/// Ce rămâne e pârghia inversă — se scoate acum, de aici, când e nepotrivit. Fără logo se afișează
/// inițialele firmei, deci nimic nu se rupe.
/// </remarks>
public sealed record RemoveCompanyLogoCommand(Guid ProfileId) : ICommand<AdminCompanyPageDetail>;

internal sealed class ReviewCompanyPageCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<ReviewCompanyPageCommand, AdminCompanyPageDetail>
{
    public async Task<Result<AdminCompanyPageDetail>> Handle(
        ReviewCompanyPageCommand command,
        CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await AdminCompanyPageStore.Find(context, command.ProfileId, cancellationToken);
        if (profile is null)
        {
            return AdminCompanyPageStore.NotFound();
        }

        string? note = CompanyPageSanitizer.CleanText(command.Note, AdminCompanyPageStore.MaxNote);

#pragma warning disable CA1308
        switch (command.Decision?.Trim().ToLowerInvariant())
#pragma warning restore CA1308
        {
            case "approve":
                CompanyPageReview.Approve(profile, userContext.UserId, note, command.BlockedSections);
                break;

            case "reject":
                if (note is null)
                {
                    // Singurul câmp obligatoriu din tot fluxul. Proprietarul citește motivul în
                    // editorul lui; fără el, ar retrimite aceeași pagină.
                    return Result.Failure<AdminCompanyPageDetail>(Error.Problem(
                        "CompanyPage.MissingReason",
                        "Scrie motivul refuzului — proprietarul îl vede în editorul lui."));
                }

                CompanyPageReview.Reject(profile, userContext.UserId, note);
                break;

            default:
                return Result.Failure<AdminCompanyPageDetail>(Error.Problem(
                    "CompanyPage.InvalidDecision",
                    "Verdictul poate fi doar „approve” sau „reject”."));
        }

        profile.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return await AdminCompanyPageStore.Detail(context, profile, cancellationToken);
    }
}

internal sealed class SetCompanyPageSectionsCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<SetCompanyPageSectionsCommand, AdminCompanyPageDetail>
{
    public async Task<Result<AdminCompanyPageDetail>> Handle(
        SetCompanyPageSectionsCommand command,
        CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await AdminCompanyPageStore.Find(context, command.ProfileId, cancellationToken);
        if (profile is null)
        {
            return AdminCompanyPageStore.NotFound();
        }

        CompanyPageReview.SetBlockedSections(
            profile,
            userContext.UserId,
            command.BlockedSections,
            CompanyPageSanitizer.CleanText(command.Note, AdminCompanyPageStore.MaxNote));

        profile.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return await AdminCompanyPageStore.Detail(context, profile, cancellationToken);
    }
}

internal sealed class EditCompanyPageAsAdminCommandHandler(IApplicationDbContext context)
    : ICommandHandler<EditCompanyPageAsAdminCommand, AdminCompanyPageDetail>
{
    public async Task<Result<AdminCompanyPageDetail>> Handle(
        EditCompanyPageAsAdminCommand command,
        CancellationToken cancellationToken)
    {
        Result<CompanyPageTheme> theme = CompanyPageSanitizer.SanitizeTheme(command.Theme);
        if (theme.IsFailure)
        {
            return Result.Failure<AdminCompanyPageDetail>(theme.Error);
        }

        Result<CompanyPageContent> content = CompanyPageSanitizer.SanitizeContent(command.Content);
        if (content.IsFailure)
        {
            return Result.Failure<AdminCompanyPageDetail>(content.Error);
        }

        CompanyProfile? profile = await AdminCompanyPageStore.Find(context, command.ProfileId, cancellationToken);
        if (profile is null)
        {
            return AdminCompanyPageStore.NotFound();
        }

        profile.Tagline = CompanyPageSanitizer.CleanText(command.Tagline, CompanyPageSanitizer.MaxTagline);
        profile.PublicDescription =
            CompanyPageSanitizer.CleanText(command.PublicDescription, CompanyPageSanitizer.MaxDescription);
        profile.PageTheme = theme.Value;
        profile.PageContent = content.Value;

        Result pickup = UpdateCompanyPageCommandHandler.ApplyPickup(profile, command.Pickup);
        if (pickup.IsFailure)
        {
            return Result.Failure<AdminCompanyPageDetail>(pickup.Error);
        }

        profile.UpdatedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return await AdminCompanyPageStore.Detail(context, profile, cancellationToken);
    }
}

internal sealed class RemoveCompanyPageCoverCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<RemoveCompanyPageCoverCommand, AdminCompanyPageDetail>
{
    public async Task<Result<AdminCompanyPageDetail>> Handle(
        RemoveCompanyPageCoverCommand command,
        CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await AdminCompanyPageStore.Find(context, command.ProfileId, cancellationToken);
        if (profile is null)
        {
            return AdminCompanyPageStore.NotFound();
        }

        profile.CoverImageUrl = null;
        profile.PublishedPage.CoverImageUrl = null;
        profile.PageModeration.ReviewedAtUtc = DateTime.UtcNow;
        profile.PageModeration.ReviewedByUserId = userContext.UserId;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return await AdminCompanyPageStore.Detail(context, profile, cancellationToken);
    }
}

internal sealed class RemoveCompanyLogoCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<RemoveCompanyLogoCommand, AdminCompanyPageDetail>
{
    public async Task<Result<AdminCompanyPageDetail>> Handle(
        RemoveCompanyLogoCommand command,
        CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await AdminCompanyPageStore.Find(context, command.ProfileId, cancellationToken);
        if (profile is null)
        {
            return AdminCompanyPageStore.NotFound();
        }

        profile.LogoUrl = null;
        profile.PageModeration.ReviewedAtUtc = DateTime.UtcNow;
        profile.PageModeration.ReviewedByUserId = userContext.UserId;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return await AdminCompanyPageStore.Detail(context, profile, cancellationToken);
    }
}

/// <summary>Bucățile pe care le repetă toate comenzile de moderare: căutarea, eroarea, răspunsul.</summary>
internal static class AdminCompanyPageStore
{
    public const int MaxNote = 600;

    /// <summary>Urmărit, nu <c>AsNoTracking</c>: toate apelantele modifică profilul găsit.</summary>
    public static Task<CompanyProfile?> Find(
        IApplicationDbContext context,
        Guid profileId,
        CancellationToken cancellationToken) =>
        context.CompanyProfiles.FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);

    public static Result<AdminCompanyPageDetail> NotFound() =>
        Result.Failure<AdminCompanyPageDetail>(
            Error.NotFound("CompanyPage.NotFound", "Profilul firmei nu a fost găsit."));

    /// <summary>
    /// Starea de după acțiune, în aceeași formă pe care o citește lista de detaliu.
    /// </summary>
    /// <remarks>
    /// Se întoarce întreagă, nu doar un „ok": interfața de moderare arată ciorna și versiunea
    /// publicată una lângă alta, iar o reîncărcare separată după fiecare buton ar fi putut afișa
    /// pentru o clipă starea dinainte.
    /// </remarks>
    public static async Task<Result<AdminCompanyPageDetail>> Detail(
        IApplicationDbContext context,
        CompanyProfile profile,
        CancellationToken cancellationToken)
    {
        User? owner = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == profile.UserId, cancellationToken);

        int carCount = await context.Cars
            .AsNoTracking()
            .Where(c => c.PostedByUserId == profile.UserId)
            .Where(CarVisibility.IsPublic)
            .CountAsync(cancellationToken);

        return Result.Success(
            GetAdminCompanyPageQueryHandler.Map(profile, owner?.Email ?? string.Empty, carCount));
    }
}

using System.Globalization;
using Application.Abstractions.Ai;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Application.Companies.Page;
using Domain.Cars;
using Domain.Companies;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Companies.Commands.GenerateCompanyDescription;

/// <summary>
/// Propune un text de prezentare pentru mini-site, scris de model.
/// </summary>
/// <param name="Hints">
/// Ce vrea proprietarul să se spună despre firmă, în cuvintele lui. Poate lipsi.
/// </param>
/// <remarks>
/// Nu salvează nimic. Întoarce propuneri, iar omul alege și corectează — un text despre firma ta,
/// scris de altcineva și publicat fără să-l fi citit, e exact felul de ajutor pe care nu-l vrea
/// nimeni.
/// </remarks>
public sealed record GenerateCompanyDescriptionCommand(string? Hints) : ICommand<CompanyDescriptionSuggestion>;

public sealed record SuggestedHighlight(string IconKey, string Title, string Text);

public sealed record CompanyDescriptionSuggestion(
    string? Tagline,
    List<string> Descriptions,
    List<SuggestedHighlight> Highlights);

/// <summary>Forma cerută modelului. Cheile sunt snake_case, cum i se cer în prompt.</summary>
internal sealed class AiDescriptionDraft
{
    public string? Tagline { get; set; }
    public List<string> Descriptions { get; set; } = [];
    public List<AiHighlightDraft> Highlights { get; set; } = [];
}

internal sealed class AiHighlightDraft
{
    public string? IconKey { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
}

internal sealed class GenerateCompanyDescriptionCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IAiTextGenerator generator,
    IAiUsageLimiter limiter)
    : ICommandHandler<GenerateCompanyDescriptionCommand, CompanyDescriptionSuggestion>
{
    /// <summary>Cât din textul propriu al proprietarului intră în prompt.</summary>
    private const int MaxHints = 600;

    private const int MaxCallsPerWindow = 6;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public async Task<Result<CompanyDescriptionSuggestion>> Handle(
        GenerateCompanyDescriptionCommand command,
        CancellationToken cancellationToken)
    {
        CompanyProfile? profile = await context.CompanyProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.UserId == userContext.UserId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<CompanyDescriptionSuggestion>(Error.Problem(
                "CompanyDescription.NoProfile",
                "Salvează întâi datele firmei — de acolo pornește textul."));
        }

        if (!limiter.TryConsume(userContext.UserId, "company-description", MaxCallsPerWindow, Window))
        {
            return Result.Failure<CompanyDescriptionSuggestion>(Error.Problem(
                "CompanyDescription.TooMany",
                "Ai cerut multe variante într-un timp scurt. Mai încearcă peste câteva minute."));
        }

        // Doar anunțurile publice: modelul nu are voie să scrie despre mașini pe care publicul
        // nu le vede oricum pe pagină.
        List<Car> cars = await context.Cars
            .AsNoTracking()
            .Where(c => c.PostedByUserId == profile.UserId)
            .Where(CarVisibility.IsPublic)
            .ToListAsync(cancellationToken);

        string hints = (command.Hints ?? string.Empty).Trim();
        if (hints.Length > MaxHints)
        {
            hints = hints[..MaxHints];
        }

        var request = new AiTextRequest(
            SystemPrompt,
            BuildUserPrompt(profile, cars, hints),
            // Trei variante identice n-ar fi trei variante. Suficientă variație cât să difere,
            // nu atâta cât să înceapă să inventeze.
            Temperature: 0.8);

        Result<AiDescriptionDraft> draft = await generator.GenerateAsync<AiDescriptionDraft>(request, cancellationToken);

        if (draft.IsFailure)
        {
            // Mesajele interne („cheia API nu e configurată") nu ies la utilizator.
            return Result.Failure<CompanyDescriptionSuggestion>(
                draft.Error.Code == "Ai.NotConfigured"
                    ? Error.Problem("CompanyDescription.Unavailable", "Scrierea cu AI nu e disponibilă acum. Poți scrie textul manual.")
                    : Error.Problem("CompanyDescription.Failed", "N-am putut genera textul. Mai încearcă o dată."));
        }

        return Result.Success(ToSuggestion(draft.Value));
    }

    /// <summary>Taie și curăță ce a scris modelul, cu aceleași limite ca la salvare.</summary>
    private static CompanyDescriptionSuggestion ToSuggestion(AiDescriptionDraft draft)
    {
        var descriptions = (draft.Descriptions ?? [])
            .Select(d => CompanyPageSanitizer.CleanText(d, CompanyPageSanitizer.MaxDescription))
            .OfType<string>()
            .Take(3)
            .ToList();

        var highlights = (draft.Highlights ?? [])
            .Select(h => new SuggestedHighlight(
                CompanyPageIcons.Allowed.Contains(h.IconKey ?? string.Empty) ? h.IconKey! : CompanyPageIcons.Fallback,
                CompanyPageSanitizer.CleanText(h.Title, 60) ?? string.Empty,
                CompanyPageSanitizer.CleanText(h.Text, 200) ?? string.Empty))
            .Where(h => h.Title.Length > 0)
            .Take(CompanyPageSanitizer.MaxHighlights)
            .ToList();

        return new CompanyDescriptionSuggestion(
            CompanyPageSanitizer.CleanText(draft.Tagline, CompanyPageSanitizer.MaxTagline),
            descriptions,
            highlights);
    }

    /// <summary>
    /// Faptele pe care modelul are voie să le folosească — și nimic altceva.
    /// </summary>
    /// <remarks>
    /// Promptul e construit din baza de date, nu din ce își amintește modelul despre firmă:
    /// altfel un „SC Transport SRL" ar fi căpătat pe pagina publică o istorie de douăzeci de ani
    /// pe care nimeni n-a scris-o.
    /// </remarks>
    internal static string BuildUserPrompt(CompanyProfile profile, List<Car> cars, string hints)
    {
        var lines = new List<string>
        {
            $"Denumire: {profile.LegalName}",
            $"Tip: {(profile.OwnerType == OwnerType.Srl ? "societate (SRL)" : "PFA")}",
        };

        if (!string.IsNullOrWhiteSpace(profile.RegisteredOffice))
        {
            lines.Add($"Sediu: {profile.RegisteredOffice}");
        }

        if (profile.IsVerified)
        {
            lines.Add("Flota e verificată de RIDElance.");
        }

        lines.Add($"Mașini publicate acum: {cars.Count.ToString(CultureInfo.InvariantCulture)}");

        if (cars.Count > 0)
        {
            string brands = string.Join(", ", cars.Select(c => c.Brand).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().Take(8));
            if (brands.Length > 0)
            {
                lines.Add($"Mărci: {brands}");
            }

            decimal min = cars.Min(c => c.PricePerWeek);
            decimal max = cars.Max(c => c.PricePerWeek);
            if (min > 0)
            {
                lines.Add(min == max
                    ? $"Preț: {min.ToString("0", CultureInfo.InvariantCulture)} lei/săptămână"
                    : $"Prețuri: între {min.ToString("0", CultureInfo.InvariantCulture)} și {max.ToString("0", CultureInfo.InvariantCulture)} lei/săptămână");
            }

            int newest = cars.Max(c => c.Year);
            int oldest = cars.Min(c => c.Year);
            if (oldest > 1950)
            {
                lines.Add($"Ani de fabricație: {oldest.ToString(CultureInfo.InvariantCulture)}–{newest.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        string facts = string.Join("\n", lines);

        return hints.Length == 0
            ? $"Datele firmei:\n{facts}\n\nProprietarul nu a adăugat indicații proprii."
            : $"Datele firmei:\n{facts}\n\nCe vrea proprietarul să se spună, în cuvintele lui:\n{hints}";
    }

    private const string SystemPrompt =
        "Ești copywriter pentru RIDElance, o platformă din România unde firmele închiriază mașini șoferilor de " +
        "ridesharing. Scrii textul de prezentare al paginii publice a unei flote. " +
        "Primești datele reale ale firmei și, uneori, câteva indicații scrise chiar de proprietar. " +
        "REGULA PRINCIPALĂ: nu inventa NIMIC. Nu promite asigurare inclusă, livrare, mentenanță gratuită, " +
        "reduceri, vechime pe piață, număr de clienți sau premii dacă nu apar explicit în datele primite sau " +
        "în indicațiile proprietarului. Textul ajunge pe o pagină publică și devine o promisiune. " +
        "Scrie în română corectă, cu diacritice, la persoana I plural („oferim”, „predăm”), pe un ton profesionist " +
        "și direct, fără superlative goale și fără exclamații. " +
        "Răspunde STRICT cu un obiect JSON valid, fără alt text, în exact acest format: " +
        "{\"tagline\": \"un slogan de maximum 80 de caractere\", " +
        "\"descriptions\": [\"trei variante distincte de text, fiecare între 400 și 700 de caractere\"], " +
        "\"highlights\": [{\"icon_key\": \"cheie\", \"title\": \"maximum 6 cuvinte\", \"text\": \"o frază\"}]}. " +
        "Dă exact 3 variante în \"descriptions\", diferite ca structură, nu reformulări ale aceleiași fraze. " +
        "Dă cel mult 4 avantaje în \"highlights\", fiecare susținut de date sau de indicațiile proprietarului. " +
        "\"icon_key\" se alege DOAR din lista: check, shield, clock, wallet, car, phone, star, wrench, map, bolt.";
}

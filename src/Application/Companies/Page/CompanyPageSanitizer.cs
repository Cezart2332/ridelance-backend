using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Domain.Companies;
using SharedKernel;

namespace Application.Companies.Page;

/// <summary>
/// Curăță și validează personalizarea mini-site-ului înainte de salvare.
/// </summary>
/// <remarks>
/// Rulează pe server, nu doar în editor, fiindcă rezultatul ajunge pe o pagină publică: orice
/// verificare făcută doar în browser e o verificare pe care o poate sări oricine trimite cererea
/// direct.
///
/// Distincția între „curăț" și „refuz" e deliberată. Spațiile în plus, rândurile goale și textele
/// prea lungi se rezolvă tăcut — omul n-are ce învăța dintr-o eroare pentru un rând gol. O culoare
/// care nu e un hex valid sau o listă peste plafon se refuză cu mesaj: acolo s-a pierdut ceva ce
/// utilizatorul chiar a scris, iar tăcerea ar fi arătat ca o salvare reușită care nu s-a salvat.
/// </remarks>
internal static partial class CompanyPageSanitizer
{
    public const int MaxTagline = 160;
    public const int MaxDescription = 2048;
    public const int MaxHighlights = 6;
    public const int MaxScheduleRows = 7;
    public const int MaxCoverageAreas = 12;
    public const int MaxFaq = 8;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    /// <summary>Numele câmpurilor de culoare, pentru mesajul de eroare.</summary>
    private static readonly (string Label, Func<CompanyPageTheme, string> Read, Action<CompanyPageTheme, string> Write)[] Colors =
    [
        ("Accent", t => t.Accent, (t, v) => t.Accent = v),
        ("Fundal", t => t.Background, (t, v) => t.Background = v),
        ("Suprafață", t => t.Surface, (t, v) => t.Surface = v),
        ("Text", t => t.Text, (t, v) => t.Text = v),
        ("Text pe butoane", t => t.ButtonText, (t, v) => t.ButtonText = v),
        ("Văl peste cover", t => t.HeroOverlay, (t, v) => t.HeroOverlay = v),
    ];

    public static Result<CompanyPageTheme> SanitizeTheme(CompanyPageTheme? theme)
    {
        var clean = new CompanyPageTheme();
        if (theme is null)
        {
            return Result.Success(clean);
        }

        foreach ((string label, Func<CompanyPageTheme, string> read, Action<CompanyPageTheme, string> write) in Colors)
        {
            string value = (read(theme) ?? string.Empty).Trim();
            if (!HexColor().IsMatch(value))
            {
                return Result.Failure<CompanyPageTheme>(Error.Problem(
                    "CompanyPage.InvalidColor",
                    $"Culoarea „{label}” nu e validă. Se așteaptă forma #RRGGBB."));
            }

            write(clean, value.ToUpperInvariant());
        }

        // Opacitatea se limitează, nu se refuză: e un cursor, iar o valoare din afara intervalului
        // e o greșeală de client, nu o alegere a omului. 90 e plafonul ca fotografia să rămână
        // vizibilă — un văl opac ar fi însemnat că nu mai există cover.
        clean.HeroOverlayOpacity = Math.Clamp(theme.HeroOverlayOpacity, 0, 90);

        return Result.Success(clean);
    }

    public static Result<CompanyPageContent> SanitizeContent(CompanyPageContent? content)
    {
        var clean = new CompanyPageContent();
        if (content is null)
        {
            return Result.Success(clean);
        }

        Result<CompanyPageContent> tooMany = CheckCaps(content);
        if (tooMany.IsFailure)
        {
            return tooMany;
        }

        foreach (CompanyPageHighlight highlight in content.Highlights ?? [])
        {
            string title = Clean(highlight.Title, 60);
            string text = Clean(highlight.Text, 200, allowNewLines: true);
            if (title.Length == 0 && text.Length == 0)
            {
                continue;
            }

            clean.Highlights.Add(new CompanyPageHighlight
            {
                // O cheie necunoscută nu e un motiv de eroare: se cade pe bifă, iar avantajul
                // rămâne pe pagină. Textul e ce contează acolo, nu iconița.
                IconKey = CompanyPageIcons.Allowed.Contains(highlight.IconKey ?? string.Empty)
                    ? highlight.IconKey!
                    : CompanyPageIcons.Fallback,
                Title = title,
                Text = text,
            });
        }

        foreach (CompanyPageScheduleRow row in content.Schedule ?? [])
        {
            string day = Clean(row.Day, 40);
            string hours = Clean(row.Hours, 60);
            if (day.Length == 0 && hours.Length == 0)
            {
                continue;
            }

            clean.Schedule.Add(new CompanyPageScheduleRow { Day = day, Hours = hours });
        }

        foreach (string area in content.CoverageAreas ?? [])
        {
            string value = Clean(area, 60);
            if (value.Length == 0)
            {
                continue;
            }

            clean.CoverageAreas.Add(value);
        }

        foreach (CompanyPageFaq entry in content.Faq ?? [])
        {
            string question = Clean(entry.Question, 160);
            string answer = Clean(entry.Answer, 800, allowNewLines: true);
            if (question.Length == 0 || answer.Length == 0)
            {
                // Aici, spre deosebire de avantaje, jumătatea de rând n-are ce afișa: o întrebare
                // fără răspuns e o secțiune care pare ruptă.
                continue;
            }

            clean.Faq.Add(new CompanyPageFaq { Question = question, Answer = answer });
        }

        string note = Clean(content.CoverageNote, 400, allowNewLines: true);
        clean.CoverageNote = note.Length == 0 ? null : note;

        return Result.Success(clean);
    }

    public static string? CleanText(string? value, int maxLength)
    {
        string cleaned = Clean(value, maxLength, allowNewLines: true);
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static Result<CompanyPageContent> CheckCaps(CompanyPageContent content)
    {
        (int Count, int Max, string Label)[] caps =
        [
            (content.Highlights?.Count ?? 0, MaxHighlights, $"{MaxHighlights} avantaje"),
            (content.Schedule?.Count ?? 0, MaxScheduleRows, $"{MaxScheduleRows} rânduri de program"),
            (content.CoverageAreas?.Count ?? 0, MaxCoverageAreas, $"{MaxCoverageAreas} zone de predare"),
            (content.Faq?.Count ?? 0, MaxFaq, $"{MaxFaq} întrebări frecvente"),
        ];

        foreach ((int count, int max, string label) in caps)
        {
            if (count > max)
            {
                return Result.Failure<CompanyPageContent>(Error.Problem(
                    "CompanyPage.TooManyItems",
                    $"Poți avea cel mult {label}."));
            }
        }

        return Result.Success(content);
    }

    /// <summary>
    /// Taie spațiile, scoate caracterele de control și limitează lungimea.
    /// </summary>
    /// <remarks>
    /// Caracterele de control se scot fiindcă textul se randează pe o pagină publică și n-au ce
    /// căuta acolo nici măcar invizibile. Rândurile noi se păstrează doar unde au sens — într-un
    /// titlu de avantaj, un enter e o greșeală de lipit, nu o intenție.
    /// </remarks>
    private static string Clean(string? value, int maxLength, bool allowNewLines = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '\n' && allowNewLines)
            {
                builder.Append('\n');
                continue;
            }

            if (char.GetUnicodeCategory(c) == UnicodeCategory.Control)
            {
                continue;
            }

            builder.Append(c);
        }

        string cleaned = builder.ToString().Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }
}

using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Dossiers;

/// <summary>
/// Bucățile comune tuturor PDF-urilor RIDElance: pagina, antetul, subsolul, rândul etichetă-valoare.
/// </summary>
/// <remarks>
/// Erau copiate în fiecare generator — `Frame`, `Row`, `Join`, cultura `ro-RO` — pentru că fiecare
/// era `internal sealed` și nimeni n-avea de unde le moșteni. Rezultatul: trei documente ale
/// aceleiași firme puteau avea trei antete ușor diferite, iar o schimbare de marcă cerea trei
/// modificări identice.
/// </remarks>
internal static class PdfPrimitives
{
    public static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    /// <summary>Pagina standard: A4, margini, antet cu marca și titlul, subsol cu momentul generării.</summary>
    public static void Frame(PageDescriptor page, string title, DateTime generatedAtUtc, string? subtitle = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        page.Size(PageSizes.A4);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

        page.Header().Column(header =>
        {
            header.Item().Text("RIDElance").Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
            header.Item().Text(title).SemiBold().FontSize(13);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                header.Item().Text(subtitle).FontSize(10).FontColor(Colors.Grey.Darken1);
            }
        });

        page.Footer().AlignRight()
            .Text($"Generat de RIDElance · {generatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Ro)}")
            .FontSize(9)
            .FontColor(Colors.Grey.Medium);
    }

    /// <summary>Un rând etichetă-valoare. Eticheta pe lățime fixă, ca valorile să se alinieze.</summary>
    public static void Row(ColumnDescriptor col, string label, string? value)
    {
        ArgumentNullException.ThrowIfNull(col);

        col.Item().Row(row =>
        {
            row.ConstantItem(160).Text(label).SemiBold();
            // Câmpul gol se scrie ca liniuță, nu se lasă alb: pe hârtie, spațiul gol nu se poate
            // deosebi de o eroare de tipărire.
            row.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
        });
    }

    /// <summary>Titlu de secțiune în corpul documentului.</summary>
    public static void Section(ColumnDescriptor col, string title)
    {
        ArgumentNullException.ThrowIfNull(col);
        col.Item().PaddingTop(6).Text(title).SemiBold().FontSize(12);
    }

    public static string Join(params string?[] parts)
    {
        string joined = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrEmpty(joined) ? "—" : joined;
    }

    /// <summary>Bani în lei, cu separator de mii. Sumele din platformă sunt întotdeauna în bani.</summary>
    public static string Lei(long bani) => (bani / 100m).ToString("N2", Ro) + " lei";
}

using System.Text;

namespace Infrastructure.Dossiers.Latex;

/// <summary>
/// Trecerea textului din platformă în sursă LaTeX.
/// </summary>
/// <remarks>
/// Aici stă singura barieră dintre ce scrie un utilizator și un motor care execută ce citește.
/// Un nume de firmă care conține <c>\input</c> sau <c>%</c> nu are voie să ajungă comandă sau
/// comentariu; de aceea escaparea se face caracter cu caracter, nu prin înlocuiri succesive —
/// înlocuirile succesive rescriu backslash-urile introduse de ele însele.
/// </remarks>
internal static class LatexText
{
    /// <summary>Textul unui câmp: escapat și adus pe un singur rând.</summary>
    /// <remarks>
    /// Valorile se tipăresc în celule de tabel, unde un rând nou rupe alinierea. Observațiile
    /// scrise pe mai multe rânduri se citesc la fel de bine curgând într-un paragraf.
    /// </remarks>
    public static string Inline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Escape(string.Join(' ', value.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
    }

    /// <summary>
    /// Text liber, cu paragrafele păstrate: rândul gol desparte paragrafe, rândul simplu rupe linia.
    /// </summary>
    /// <remarks>
    /// Condițiile de închiriere se scriu aproape întotdeauna ca listă numerotată, rând cu rând.
    /// Dacă rândurile s-ar contopi, textul ar deveni un bloc din care nu se mai vede unde începe
    /// obligația următoare.
    /// </remarks>
    public static IReadOnlyList<string> Paragraphs(string value)
    {
        string normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        List<string> paragraphs = [];

        foreach (string block in normalized.Split("\n\n", StringSplitOptions.TrimEntries))
        {
            string[] lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (lines.Length == 0)
            {
                continue;
            }

            // `\\{}` în loc de `\\`: grupul gol oprește căutarea argumentului opțional, altfel un
            // rând care începe cu „[" ar fi înghițit ca argument al ruperii de rând.
            paragraphs.Add(string.Join(@"\\{}" + '\n', lines.Select(Escape)));
        }

        return paragraphs;
    }

    /// <summary>Escaparea propriu-zisă a celor zece caractere cu înțeles în LaTeX.</summary>
    public static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length + 16);

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': builder.Append(@"\textbackslash{}"); break;
                case '~': builder.Append(@"\textasciitilde{}"); break;
                case '^': builder.Append(@"\textasciicircum{}"); break;
                case '{' or '}' or '$' or '&' or '%' or '#' or '_':
                    builder.Append('\\').Append(c);
                    break;
                default:
                    // Caracterele de control nu au reprezentare tipografică, dar au efecte în
                    // motorul TeX. Tab-ul devine spațiu, restul dispar.
                    if (c == '\t')
                    {
                        builder.Append(' ');
                    }
                    else if (!char.IsControl(c))
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}

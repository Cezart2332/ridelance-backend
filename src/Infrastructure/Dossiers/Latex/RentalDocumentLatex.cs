using System.Globalization;
using System.Text;
using Application.Abstractions.Dossiers;

namespace Infrastructure.Dossiers.Latex;

/// <summary>
/// Sursa LaTeX a unui document de închiriere.
/// </summary>
/// <remarks>
/// Șablonul e deliberat gol de marcă: fără siglă, fără culori, fără antet și fără subsol de
/// platformă. Contractul se încheie între firma de flotă și chiriașul ei — RIDElance nu e parte în
/// el, deci nu are ce căuta tipărit pe el.
/// <para>
/// Locul semnăturii se lasă gol la generare și se umple la retipărire, dacă lângă sursă se găsește
/// fișierul <c>semnatura-N.png</c>, cu mențiunea din <c>mentiune-N.tex</c> dedesubt. Sursa nu se
/// modifică niciodată: documentul semnat și cel nesemnat ies din exact aceleași rânduri, singura
/// diferență fiind fișierele de alături.
/// </para>
/// </remarks>
internal static class RentalDocumentLatex
{
    /// <summary>Câmpul gol se tipărește ca linie, nu ca spațiu alb.</summary>
    /// <remarks>Pe hârtie, un loc lăsat gol nu se poate deosebi de o greșeală de tipar.</remarks>
    private const string Empty = "---";

    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public static string Build(RentalDocumentData data)
    {
        var tex = new StringBuilder(4096);

        tex.AppendLine(Preamble);
        tex.AppendLine(@"\begin{document}");

        tex.AppendLine(@"\begin{center}");
        tex.Append(@"{\LARGE\bfseries ").Append(LatexText.Inline(data.Title)).AppendLine(@"}\\[6pt]");
        tex.Append("Nr. ").Append(LatexText.Inline(data.PublicCode))
           .Append(@" \quad ")
           .AppendLine(data.GeneratedAtUtc.ToLocalTime().ToString("dd.MM.yyyy", Ro));
        tex.AppendLine(@"\end{center}");

        foreach (RentalDocumentSection section in data.Sections)
        {
            Section(tex, section.Title);
            tex.AppendLine(@"\begin{campuri}");

            foreach (RentalDocumentField field in section.Fields)
            {
                string value = LatexText.Inline(field.Value);
                tex.Append(LatexText.Inline(field.Label))
                   .Append(" & ")
                   .Append(value.Length == 0 ? Empty : value)
                   .AppendLine(@" \\");
            }

            tex.AppendLine(@"\end{campuri}");
        }

        if (!string.IsNullOrWhiteSpace(data.Clauses))
        {
            Section(tex, "Condiții");

            foreach (string paragraph in LatexText.Paragraphs(data.Clauses))
            {
                tex.Append(@"\alineat{").Append(paragraph).AppendLine("}");
            }
        }

        Signatures(tex, data.SignatureLines);

        tex.AppendLine(@"\end{document}");

        return tex.ToString();
    }

    /// <summary>Numele sub care șablonul caută semnătura de pe linia <paramref name="slot"/>.</summary>
    public static string SignatureFileName(int slot) =>
        string.Create(CultureInfo.InvariantCulture, $"semnatura-{slot}.png");

    /// <summary>Numele sub care caută mențiunea de sub aceeași linie.</summary>
    public static string SignatureNoteFileName(int slot) =>
        string.Create(CultureInfo.InvariantCulture, $"mentiune-{slot}.tex");

    private static void Section(StringBuilder tex, string title) =>
        tex.Append(@"\sectiune{").Append(LatexText.Inline(title)).AppendLine("}");

    private static void Signatures(StringBuilder tex, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        // Se poate strânge, ca semnăturile să nu ajungă singure pe o pagină nouă când documentul
        // se termină aproape de marginea de jos. Spațiul de semnat propriu-zis stă în `\semnatura`.
        tex.AppendLine(@"\par\vspace{24pt minus 18pt}");
        tex.Append(@"\noindent\begin{tabularx}{\linewidth}{@{}")
           .Append(string.Join(@"@{\hspace{1.4cm}}", Enumerable.Repeat(@">{\centering\arraybackslash}X", lines.Count)))
           .AppendLine("@{}}");

        // Locul semnăturii. Gol, ține un spațiu de exact aceeași înălțime, ca varianta semnată și
        // cea nesemnată să fie același document, nu două paginări diferite.
        tex.AppendLine(string.Join(" & ", Enumerable.Range(1, lines.Count).Select(i => $@"\semnatura{{{i}}}")) + @" \\");
        tex.AppendLine(string.Join(" & ", Enumerable.Repeat(@"\rule{\linewidth}{0.4pt}", lines.Count)) + @" \\");
        tex.AppendLine(string.Join(" & ", lines.Select(LatexText.Inline)) + @" \\");
        tex.AppendLine(string.Join(" & ", Enumerable.Range(1, lines.Count).Select(i => $@"\mentiune{{{i}}}")) + @" \\");
        tex.AppendLine(@"\end{tabularx}");
    }

    /// <summary>
    /// Preambulul: A4, un font cu diacritice românești și două comenzi proprii.
    /// </summary>
    /// <remarks>
    /// Fontul se alege explicit prin `fontspec`, nu se lasă pe cel implicit al motorului: fonturile
    /// clasice TeX nu au ș și ț cu virgulă ca glife proprii, ci le compun din literă și accent, iar
    /// PDF-ul rezultat arată bine dar nu se poate căuta și nu se poate copia corect.
    /// <para>
    /// `campuri` se deschide cu `\tabularx`, nu cu `\begin{tabularx}`: tabularx își citește corpul
    /// căutând textual `\end{tabularx}`, așa că nu poate fi împachetat altfel.
    /// </para>
    /// </remarks>
    private const string Preamble = """
        \documentclass[11pt,a4paper]{article}
        \usepackage[a4paper,margin=2.2cm]{geometry}
        \usepackage{fontspec}
        \usepackage{tabularx}
        \usepackage{graphicx}
        \defaultfontfeatures{Ligatures=TeX}
        \setmainfont{Latin Modern Roman}
        \setlength{\parindent}{0pt}
        \setlength{\parskip}{0pt}
        \linespread{1.05}
        \pagestyle{plain}
        \newenvironment{campuri}
          {\tabularx{\linewidth}{@{}>{\bfseries}p{5.2cm}X@{}}}
          {\endtabularx}
        \newcommand{\sectiune}[1]{\par\vspace{16pt}{\large\bfseries #1}\par\vspace{6pt}}
        \newcommand{\alineat}[1]{\par\vspace{6pt}#1\par}
        \newcommand{\semnatura}[1]{\IfFileExists{semnatura-#1.png}%
          {\includegraphics[width=\linewidth,height=1.3cm,keepaspectratio]{semnatura-#1.png}}%
          {\rule{0pt}{1.3cm}}}
        \newcommand{\mentiune}[1]{\IfFileExists{mentiune-#1.tex}{\footnotesize\input{mentiune-#1.tex}}{}}
        """;
}

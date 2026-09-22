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

        string date = data.GeneratedAtUtc.ToLocalTime().ToString("dd.MM.yyyy", Ro);

        if (data.Articles is { Count: > 0 } articles)
        {
            // Textul șablonului de contract: capitole, paragrafe numerotate, valorile completate.
            Heading(tex, data.Kicker, data.Title, data.Kicker is null ? $"Nr. {data.PublicCode} / {date}" : null);
            bool signed = false;
            foreach (RentalDocumentArticle article in articles)
            {
                Article(tex, article);
                signed |= article.Signatures is not null;
            }

            if (!string.IsNullOrWhiteSpace(data.Clauses))
            {
                Section(tex, "Clauze suplimentare");
                foreach (string paragraph in LatexText.Paragraphs(data.Clauses))
                {
                    tex.Append(@"\alineat{").Append(paragraph).AppendLine("}");
                }
            }

            if (!signed)
            {
                Signatures(tex, data.SignatureLines);
            }

            tex.AppendLine(@"\end{document}");
            return tex.ToString();
        }

        tex.AppendLine(@"\begin{center}");
        tex.Append(@"{\LARGE\bfseries ").Append(LatexText.Inline(data.Title)).AppendLine(@"}\\[6pt]");
        tex.Append("Nr. ").Append(LatexText.Inline(data.PublicCode))
           .Append(@" \quad ")
           .AppendLine(date);
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

    /// <summary>Titlul unui document (sau al anexei din același PDF): rândul mic, titlul, numărul.</summary>
    private static void Heading(StringBuilder tex, string? kicker, string title, string? number)
    {
        tex.AppendLine(@"\begin{center}");
        if (!string.IsNullOrWhiteSpace(kicker))
        {
            tex.Append(@"{\bfseries ").Append(LatexText.Inline(kicker)).AppendLine(@"}\\[10pt]");
        }

        tex.Append(@"{\Large\bfseries ").Append(LatexText.Inline(title)).AppendLine("}");
        if (!string.IsNullOrWhiteSpace(number))
        {
            tex.Append(@"\\[6pt]").AppendLine(LatexText.Inline(number));
        }

        tex.AppendLine(@"\end{center}");
    }

    private static void Article(StringBuilder tex, RentalDocumentArticle article)
    {
        if (article.NewPage)
        {
            tex.AppendLine(@"\newpage");
        }

        if (!string.IsNullOrWhiteSpace(article.Heading))
        {
            Heading(tex, article.Title, article.Heading, null);
        }
        else if (!string.IsNullOrWhiteSpace(article.Title))
        {
            tex.Append(@"\capitol{").Append(LatexText.Inline(article.Title)).AppendLine("}");
        }

        foreach (IReadOnlyList<RentalTextRun> paragraph in article.Paragraphs)
        {
            tex.Append(@"\alineat{");
            foreach (RentalTextRun run in paragraph)
            {
                tex.Append(Run(run));
            }

            tex.AppendLine("}");
        }

        if (article.Signatures is { } block)
        {
            Signatures(tex, block.Names, block.Captions);
        }
    }

    /// <summary>
    /// O bucată de paragraf. Valorile completate se îngroașă, ca pe hârtie să se vadă ce a pus
    /// sistemul și ce e textul șablonului; locurile goale rămân linii de completat de mână.
    /// </summary>
    private static string Run(RentalTextRun run) => run.Kind switch
    {
        RentalTextKind.Value => @"\textbf{" + LatexText.Inline(run.Text) + "}",
        RentalTextKind.Blank => Blank(run.Text),
        RentalTextKind.Checkbox => run.Text.Length > 0 ? @"$\boxtimes$" : @"$\square$",
        _ => KeepEdgeSpaces(run.Text),
    };

    /// <summary>
    /// Textul șablonului, escapat, cu spațiile de la capete păstrate. <see cref="LatexText.Inline" />
    /// le taie — bine pentru o celulă de tabel, greșit aici: textul se lipește de valoarea vecină
    /// („perioada	extbf{de 28 de zile}”).
    /// </summary>
    private static string KeepEdgeSpaces(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text.Length > 0 ? " " : string.Empty;
        }

        string lead = char.IsWhiteSpace(text[0]) ? " " : string.Empty;
        string trail = char.IsWhiteSpace(text[^1]) ? " " : string.Empty;
        return lead + LatexText.Inline(text) + trail;
    }

    /// <summary>
    /// O linie goală. Una lungă (observații, avarii) nu se poate rupe pe două rânduri și ar ieși din
    /// pagină lângă text, deci trece pe rândul ei, pe toată lățimea.
    /// </summary>
    private static string Blank(string width)
    {
        string safe = BlankWidth(width);
        bool wide = double.TryParse(safe.AsSpan(0, safe.Length - 2), System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out double cm) && cm > 8;
        return wide ? @"\newline\gol{\linewidth}" : @"\gol{" + safe + "}";
    }

    /// <summary>Lățimea unei linii goale. Doar valori de forma „3cm”, niciodată text de utilizator.</summary>
    private static string BlankWidth(string width) =>
        System.Text.RegularExpressions.Regex.IsMatch(width, @"^\d+(\.\d+)?cm$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(50))
            ? width
            : "3cm";

    private static void Section(StringBuilder tex, string title) =>
        tex.Append(@"\sectiune{").Append(LatexText.Inline(title)).AppendLine("}");

    private static void Signatures(StringBuilder tex, IReadOnlyList<string> lines, IReadOnlyList<string>? captions = null)
    {
        if (lines.Count == 0)
        {
            return;
        }

        bool withCaptions = captions is { Count: > 0 };

        // Se poate strânge, ca semnăturile să nu ajungă singure pe o pagină nouă când documentul
        // se termină aproape de marginea de jos. Spațiul de semnat propriu-zis stă în `\semnatura`.
        tex.AppendLine(@"\par\vspace{24pt minus 18pt}");
        tex.Append(@"\noindent\begin{tabularx}{\linewidth}{@{}")
           .Append(string.Join(@"@{\hspace{1.4cm}}", Enumerable.Repeat(@">{\centering\arraybackslash}X", lines.Count)))
           .AppendLine("@{}}");

        // Rolul părții deasupra, ca în șablon: „LOCATOR”, „AM PREDAT, LOCATOR”.
        if (withCaptions)
        {
            tex.AppendLine(string.Join(" & ", captions!.Select(c => @"\textbf{" + LatexText.Inline(c) + "}")) + @" \\[4pt]");
        }

        // Locul semnăturii. Gol, ține un spațiu de exact aceeași înălțime, ca varianta semnată și
        // cea nesemnată să fie același document, nu două paginări diferite.
        tex.AppendLine(string.Join(" & ", Enumerable.Range(1, lines.Count).Select(i => $@"\semnatura{{{i}}}")) + @" \\");
        tex.AppendLine(string.Join(" & ", Enumerable.Repeat(@"\rule{\linewidth}{0.4pt}", lines.Count)) + @" \\");
        tex.AppendLine(string.Join(" & ", lines.Select(line => (withCaptions ? "Nume: " : string.Empty) + LatexText.Inline(line))) + @" \\");
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
        \usepackage{amssymb}
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
        \newcommand{\capitol}[1]{\par\vspace{14pt}{\centering\bfseries #1\par}\vspace{4pt}}
        \newcommand{\gol}[1]{\underline{\hspace{#1}}}
        \newcommand{\semnatura}[1]{\IfFileExists{semnatura-#1.png}%
          {\includegraphics[width=\linewidth,height=1.3cm,keepaspectratio]{semnatura-#1.png}}%
          {\rule{0pt}{1.3cm}}}
        \newcommand{\mentiune}[1]{\IfFileExists{mentiune-#1.tex}{\footnotesize\input{mentiune-#1.tex}}{}}
        """;
}

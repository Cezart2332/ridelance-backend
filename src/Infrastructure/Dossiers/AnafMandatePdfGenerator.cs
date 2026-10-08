using System.Globalization;
using Application.Abstractions.Dossiers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Dossiers;

/// <summary>
/// Împuternicirea ANAF. Textul e cel din modelul RIDElance („Imputernicire_ANAF_RIDElance”),
/// cuvânt cu cuvânt; se schimbă doar datele dintre acolade. Un câmp lipsă iese „—”, ca adminul să-l
/// vadă pe hârtie, nu doar în listă.
/// </summary>
internal sealed class AnafMandatePdfGenerator : IAnafMandatePdfGenerator
{
    private const string Missing = "—";
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    private static readonly string[] Powers =
    [
        "să completeze, semneze electronic cu propriul certificat digital calificat și să depună, în numele Mandantului, " +
        "declarații fiscale, declarații de înregistrare sau de mențiuni, cereri, formulare, declarații rectificative și alte " +
        "documente fiscale;",
        "să efectueze formalitățile necesare înregistrării și utilizării certificatului digital calificat pentru serviciile " +
        "electronice ANAF și pentru depunerea online a declarațiilor, inclusiv formalitățile aferente formularului 150;",
        "să efectueze formalitățile privind înregistrarea, modificarea și actualizarea vectorului fiscal, inclusiv " +
        "depunerea formularului D700 și înregistrarea specială în scopuri de TVA potrivit art. 317 din Codul fiscal, atunci " +
        "când este aplicabilă;",
        "să depună declarațiile fiscale aplicabile activității Mandantului, inclusiv D100, D301, D390, D212, precum și " +
        "formularele care le înlocuiesc sau devin aplicabile ulterior;",
        "să acceseze și să utilizeze serviciile electronice ANAF și Spațiul Privat Virtual, în limitele drepturilor acordate " +
        "de ANAF, inclusiv pentru consultarea dosarului fiscal și a informațiilor aferente Mandantului;",
        "să primească, consulte și descarce recipise, decizii, notificări, somații, comunicări, răspunsuri, obligații de " +
        "plată, situații sintetice, vectorul fiscal, istoricul declarațiilor și orice alte documente ori informații fiscale " +
        "aferente Mandantului;",
        "să formuleze și să transmită solicitări, răspunsuri și documente către ANAF și să îndeplinească actele " +
        "administrative necesare executării prezentului mandat;",
        "să acceseze, transmită și descarce documente prin serviciile RO e-Factura, în măsura în care acest drept este " +
        "disponibil și necesar pentru îndeplinirea obligațiilor fiscale ale Mandantului.",
    ];

    private static readonly string[] Closing =
    [
        "Prezenta împuternicire nu conferă Mandatarului dreptul de a dispune de conturile bancare, numerarul ori " +
        "bunurile Mandantului și nici de a încheia contracte în numele acestuia, în afara actelor strict necesare " +
        "reprezentării fiscale în relația cu ANAF.",
        "Prezenta împuternicire este valabilă timp de 10 ani de la data semnării, dacă nu este revocată anterior de " +
        "Mandant sau dacă nu intervine o altă cauză legală de încetare. Mandantul o poate revoca în orice moment, în " +
        "condițiile legii.",
        "Prezenta împuternicire poate fi utilizată în format electronic ca document justificativ pentru înregistrarea și " +
        "exercitarea drepturilor de reprezentare fiscală acordate Mandatarului.",
    ];

    public byte[] Generate(AnafMandateData data)
    {
        AnafMandant m = data.Mandant;
        AnafMandatar r = data.Mandatar;
        string date = data.Date.ToString("dd.MM.yyyy", Ro);

        string onrc = string.IsNullOrWhiteSpace(m.RegistryNumber)
            ? string.Empty
            : $", înregistrat la ONRC sub nr. {m.RegistryNumber.Trim()}";

        string mandant =
            $"Subsemnatul/Subsemnata {V(m.FullName)}, CNP {V(m.Cnp)}, domiciliat(ă) în {V(m.Domicile)}, " +
            $"identificat(ă) cu CI seria {V(m.IdSeries)} nr. {V(m.IdNumber)}, eliberată de {V(m.IdIssuer)} " +
            $"la data de {(m.IdIssuedOn is { } issued ? issued.ToString("dd.MM.yyyy", Ro) : Missing)}, " +
            $"în calitate de titular al {V(m.PfaName)}, cu sediul profesional în {V(m.ProfessionalOffice)}, " +
            $"CUI {V(m.Cui)}{onrc}, denumit(ă) în continuare Mandant, îl împuternicesc prin prezenta pe:";

        string mandatar =
            $"{V(r.FullName)}, CNP {V(r.Cnp)}, domiciliat în {V(r.Domicile)}, identificat cu CI seria {V(r.IdSeries)} " +
            $"nr. {V(r.IdNumber)}, e-mail {V(r.Email)}, denumit în continuare Mandatar,";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.8f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).LineHeight(1.2f).FontColor(Colors.Black));

                page.Content().Column(col =>
                {
                    col.Spacing(7);

                    col.Item().AlignCenter().Text("ÎMPUTERNICIRE").Bold().FontSize(14);
                    col.Item().AlignCenter().Text($"Nr. {data.Number} / {date}");
                    col.Item().Height(6);

                    Paragraph(col, mandant);
                    Paragraph(col, mandatar);
                    Paragraph(col,
                        "să mă reprezinte în relația cu Agenția Națională de Administrare Fiscală (ANAF) și cu organele fiscale " +
                        "competente, în nume propriu și în calitate de titular al PFA-ului menționat mai sus, în limitele prezentei " +
                        "împuterniciri.");
                    Paragraph(col, "În acest scop, Mandatarul este împuternicit:");

                    for (int i = 0; i < Powers.Length; i++)
                    {
                        Paragraph(col, $"{(i + 1).ToString(CultureInfo.InvariantCulture)}. {Powers[i]}");
                    }

                    foreach (string paragraph in Closing)
                    {
                        Paragraph(col, paragraph);
                    }

                    col.Item().PaddingTop(14).ShowEntire().Column(sign =>
                    {
                        sign.Spacing(2);
                        sign.Item().Text("MANDANT").Bold();
                        sign.Item().Text(V(m.FullName));
                        sign.Item().Text($"Titular {V(m.PfaName)}");
                        sign.Item().Text("Semnătură electronică calificată");
                        sign.Item().Text($"Data: {date}");
                    });
                });
            });
        }).GeneratePdf();
    }

    private static string V(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Missing : value.Trim();

    private static void Paragraph(ColumnDescriptor col, string text) =>
        col.Item().Text(t =>
        {
            t.Justify();
            t.Span(text);
        });
}

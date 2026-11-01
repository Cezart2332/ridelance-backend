using System.Globalization;
using Application.Abstractions.Dossiers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Dossiers;

/// <summary>
/// Împuternicirea ANAF. Textul e cel din modelul RIDElance („RIDElance_Model_Imputernicire_PFA”),
/// cuvânt cu cuvânt; se schimbă doar datele dintre acolade. Un câmp lipsă iese „—”, ca adminul să-l
/// vadă pe hârtie, nu doar în listă.
/// </summary>
internal sealed class AnafMandatePdfGenerator : IAnafMandatePdfGenerator
{
    private const string Missing = "—";
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public byte[] Generate(AnafMandateData data)
    {
        AnafMandant m = data.Mandant;
        AnafMandatar r = data.Mandatar;
        string date = data.Date.ToString("dd.MM.yyyy", Ro);
        string name = V(m.FullName);
        string pfa = V(m.PfaName);

        string onrc = string.IsNullOrWhiteSpace(m.RegistryNumber)
            ? string.Empty
            : $", înregistrată la ONRC sub nr. {m.RegistryNumber.Trim()}";

        string[] paragraphs =
        [
            $"Subsemnatul {name}, cetățean român, domiciliat în {V(m.Domicile)}, posesor al CI seria {V(m.IdSeries)} " +
            $"nr. {V(m.IdNumber)}, eliberată de {V(m.IdIssuer)} la data de {D(m.IdIssuedOn)}, CNP {V(m.Cnp)}, " +
            $"în nume propriu și în calitate de titular al {pfa}, cu sediul profesional în {V(m.ProfessionalOffice)}, " +
            $"CUI {V(m.Cui)}{onrc}, prin prezenta împuternicesc cu puteri depline pe:",

            $"- {V(r.FullName)}, cetățean român, domiciliat în {V(r.Domicile)}, posesor al CI seria {V(r.IdSeries)} " +
            $"nr. {V(r.IdNumber)}, eliberată de {V(r.IdIssuer)} la data de {D(r.IdIssuedOn)}, CNP {V(r.Cnp)}, " +
            "pentru ca să mă reprezinte atât în nume propriu, cât și în calitate de titular al PFA-ului mai sus menționat:",

            "În relația cu Agenția Națională de Administrare Fiscală, pentru semnarea și depunerea tuturor declarațiilor " +
            "fiscale prin mijloace electronice de transmitere la distanță, prin intermediul certificatului digital calificat " +
            "al mandatarului, în conformitate cu prevederile Ordinului Președintelui ANAF nr. 2213/2025, inclusiv pentru " +
            "formalitățile de înregistrare și utilizare a certificatului calificat. Menționez în mod expres că mandatarul are " +
            "dreptul de utilizare a serviciului de acces controlat la informațiile cuprinse în dosarul fiscal și dreptul de " +
            "înregistrare și acces în Spațiul Privat Virtual al ANAF, în conformitate cu Ordinul Președintelui ANAF " +
            "nr. 1090/2022, inclusiv dreptul de a semna declarații pe suport de hârtie și de a efectua toate operațiunile " +
            "din SPV aferente subsemnatului în nume propriu și în calitate de titular al PFA-ului. Mandatarul are dreptul " +
            "de a accesa informațiile referitoare la istoricul acțiunilor anterioare din SPV, cu mențiunea expresă că " +
            "accept termenii și condițiile de utilizare a SPV.",

            "De asemenea, mandatarul este împuternicit să mă reprezinte, atât în nume propriu, cât și în calitate de " +
            "titular al PFA-ului, în fața autorităților fiscale competente, în vederea accesării dosarului fiscal al " +
            "subsemnatului și al PFA-ului mai sus menționat, precum și a oricăror baze de date online gestionate de " +
            "autoritățile competente cu privire la evidențele fiscale ale subsemnatului " +
            $"{name} sau ale {pfa}, fiind îndeplinite cumulativ următoarele condiții:\n" +
            "a) Prezenta împuternicire este generală și privește toate operațiunile din SPV.\n" +
            "b) Împuternicirea conține acordul meu expres cu privire la accesul la informațiile referitoare la istoricul " +
            "acțiunilor anterioare din SPV al subsemnatului și al PFA-ului mai sus menționat.\n" +
            "c) Accept termenii și condițiile de utilizare a SPV.",

            "În acest scop, mandatarul este împuternicit să efectueze formalitățile legale necesare, să se prezinte în " +
            "fața tuturor organelor abilitate, să depună, să prezinte și să obțină actele necesare, să achite taxele " +
            "aferente prezentului mandat, să înregistreze cererile necesare și să facă declarații, cu respectarea " +
            "limitărilor și cerințelor legislației române în vigoare, să îndeplinească orice act sau fapt necesar și să " +
            "semneze în numele subsemnatului oriunde va fi necesar, în limitele prezentului mandat, semnătura acestuia " +
            "fiindu-mi opozabilă.",

            $"Subsemnatul {name}, în nume propriu și în calitate de titular al {pfa}, am luat act de faptul că, potrivit " +
            "art. 2016 alin. (3) din Codul civil, prezentul mandat se va întinde și asupra tuturor actelor necesare " +
            "executării lui, chiar dacă nu sunt precizate în mod expres.",

            "Prezentul mandat este gratuit, netransmisibil și valabil până la îndeplinire sau până la intervenția oricărei " +
            "cauze de încetare, conform legislației în vigoare, dar nu mai mult de 10 (zece) ani, începând de astăzi.",

            $"Prezentul înscris a fost întocmit astăzi, {date}, într-un singur exemplar.",
        ];

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10.5f).LineHeight(1.15f).FontColor(Colors.Black));

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    col.Item().AlignCenter().Column(title =>
                    {
                        title.Item().AlignCenter().Text("ÎMPUTERNICIRE").Bold().FontSize(12);
                        title.Item().AlignCenter().Text($"NR {data.Number}/{date}").Bold();
                    });
                    col.Item().Height(4);

                    foreach (string paragraph in paragraphs)
                    {
                        col.Item().Text(t =>
                        {
                            t.Justify();
                            t.Span(paragraph);
                        });
                    }

                    col.Item().PaddingTop(14).ShowEntire().Column(sign =>
                    {
                        sign.Spacing(1);
                        sign.Item().Text("Mandant,");
                        sign.Item().Text(name);
                        sign.Item().Text("în nume propriu și în calitate de titular al");
                        sign.Item().Text(pfa);
                        sign.Item().PaddingTop(12).Text("Semnătura");
                    });
                });
            });
        }).GeneratePdf();
    }

    private static string V(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Missing : value.Trim();

    private static string D(DateOnly? value) =>
        value is { } d ? d.ToString("dd.MM.yyyy", Ro) : Missing;
}

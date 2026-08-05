using System.Globalization;
using Application.Abstractions.Dossiers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Dossiers;

/// <summary>
/// Cele două PDF-uri din pachetul de export către Consulto. Fișa solicitantului nu conține
/// niciodată CNP-ul în clar — pachetul circulă prin e-mail, iar dezvăluirea CNP-ului e o
/// acțiune separată, cu audit.
/// </summary>
internal sealed class CompanyFormationPdfGenerator : ICompanyFormationPdfGenerator
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public byte[] GenerateApplicantSheet(CompanyFormationSheetData data) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                Frame(page, "Dosar de înființare — date solicitant", data.GeneratedAtUtc);

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(14);

                    foreach (CompanyFormationPerson person in data.People)
                    {
                        col.Item().Text(person.Title).SemiBold().FontSize(12);
                        col.Item().Column(fields =>
                        {
                            fields.Spacing(4);
                            foreach (CompanyFormationField field in person.Fields)
                            {
                                Row(fields, field.Label, field.Value);
                            }
                        });
                    }

                    col.Item().Text("Sediu social").SemiBold().FontSize(12);
                    col.Item().Column(office =>
                    {
                        office.Spacing(4);
                        foreach (CompanyFormationField field in data.Office)
                        {
                            Row(office, field.Label, field.Value);
                        }
                    });
                });
            });
        }).GeneratePdf();

    public byte[] GenerateConsentProof(CompanyFormationConsentProofData data) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                Frame(page, "Dovadă de consimțământ și semnătură", data.GeneratedAtUtc);

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Column(info =>
                    {
                        info.Spacing(4);
                        Row(info, "Semnatar", data.ApplicantName);
                        Row(info, "Semnat la", data.SignedAtUtc.ToString("dd.MM.yyyy HH:mm:ss 'UTC'", Ro));
                        Row(info, "Adresă IP", data.IpAddress ?? "—");
                        Row(info, "Dispozitiv", Join(data.DeviceType, data.Os, data.Browser));
                        Row(info, "User-Agent", data.UserAgent ?? "—");
                        Row(info, "Hash payload (SHA-256)", data.PayloadHash);
                    });

                    col.Item().Text("Declarații acceptate").SemiBold().FontSize(12);

                    foreach (CompanyFormationConsentLine consent in data.Consents)
                    {
                        col.Item().Column(block =>
                        {
                            block.Spacing(3);
                            block.Item().Text($"{consent.Title} (versiunea {consent.Version})").SemiBold();
                            block.Item().Text(consent.Body).FontSize(10);
                            block.Item().Text($"☑  {consent.CheckboxLabel}").FontSize(10);
                            block.Item()
                                .Text($"Acceptat la {consent.AcceptedAtUtc.ToString("dd.MM.yyyy HH:mm:ss 'UTC'", Ro)}")
                                .FontSize(9)
                                .FontColor(Colors.Grey.Medium);
                        });
                    }
                });
            });
        }).GeneratePdf();

    private static void Frame(PageDescriptor page, string title, DateTime generatedAtUtc)
    {
        page.Size(PageSizes.A4);
        page.Margin(2, Unit.Centimetre);
        page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

        page.Header().Column(header =>
        {
            header.Item().Text("RIDElance").Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
            header.Item().Text(title).SemiBold().FontSize(13);
        });

        page.Footer().AlignRight()
            .Text($"Generat de RIDElance · {generatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Ro)}")
            .FontSize(9)
            .FontColor(Colors.Grey.Medium);
    }

    private static string Join(params string?[] parts)
    {
        string joined = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.IsNullOrEmpty(joined) ? "—" : joined;
    }

    private static void Row(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(160).Text(label).SemiBold();
            row.RelativeItem().Text(value);
        });
    }
}

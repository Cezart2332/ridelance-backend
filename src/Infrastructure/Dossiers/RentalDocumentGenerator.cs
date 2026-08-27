using Application.Abstractions.Dossiers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Dossiers;

/// <summary>
/// Contractul și procesele-verbale, pe aceeași pagină standard ca restul documentelor RIDElance.
/// </summary>
internal sealed class RentalDocumentGenerator : IRentalDocumentGenerator
{
    public byte[] Generate(RentalDocumentData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                PdfPrimitives.Frame(page, data.Title, data.GeneratedAtUtc, $"Nr. {data.PublicCode}");

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(12);

                    foreach (RentalDocumentSection section in data.Sections)
                    {
                        PdfPrimitives.Section(col, section.Title);
                        col.Item().Column(fields =>
                        {
                            fields.Spacing(4);
                            foreach (RentalDocumentField field in section.Fields)
                            {
                                PdfPrimitives.Row(fields, field.Label, field.Value);
                            }
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(data.Clauses))
                    {
                        PdfPrimitives.Section(col, "Condiții");
                        col.Item().Text(data.Clauses).FontSize(10);
                    }

                    // Semnăturile stau la final, pe un rând, cu spațiu real deasupra liniei: se
                    // semnează pe hârtie la fel de des ca digital.
                    col.Item().PaddingTop(28).Row(row =>
                    {
                        foreach (string line in data.SignatureLines)
                        {
                            row.RelativeItem().Column(sig =>
                            {
                                sig.Item().Height(48);
                                sig.Item().LineHorizontal(1).LineColor(Colors.Grey.Medium);
                                sig.Item().PaddingTop(4).Text(line).FontSize(10).AlignCenter();
                            });
                            row.ConstantItem(24);
                        }
                    });
                });
            });
        }).GeneratePdf();
    }
}

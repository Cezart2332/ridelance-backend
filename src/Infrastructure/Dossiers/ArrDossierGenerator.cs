using System.Globalization;
using Application.Abstractions.Dossiers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Dossiers;

/// <summary>
/// Generatorul dosarului ARR cu QuestPDF (licență Community). Produce coperta + datele
/// solicitantului + cuprinsul documentelor, după care <see cref="DossierAssembler"/> lipește
/// documentele încărcate de driver. Formularele oficiale ARR se completează separat din
/// template-uri, ca să poată fi înlocuite fără deploy.
/// </summary>
internal sealed class ArrDossierGenerator : IDossierGenerator
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public byte[] GenerateArrDossier(ArrDossierData data)
    {
        byte[] cover = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

                page.Header().Column(header =>
                {
                    header.Item().Text("RIDElance").Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
                    header.Item().Text("Dosar autorizație de transport alternativ (ARR)")
                        .SemiBold().FontSize(13);
                });

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Column(info =>
                    {
                        info.Spacing(4);
                        Row(info, "Solicitant", data.ApplicantName);
                        if (!string.IsNullOrWhiteSpace(data.LegalName))
                        {
                            Row(info, "Denumire PFA", data.LegalName!);
                        }
                        if (!string.IsNullOrWhiteSpace(data.Cui))
                        {
                            Row(info, "CUI", data.Cui!);
                        }
                        if (!string.IsNullOrWhiteSpace(data.Address))
                        {
                            Row(info, "Adresă", data.Address!);
                        }
                        if (!string.IsNullOrWhiteSpace(data.AgencyName))
                        {
                            Row(info, "Agenție ARR", data.AgencyName!);
                        }
                        Row(info, "Taxă ARR", $"{(data.FeeBani / 100m).ToString("N2", Ro)} lei");
                    });

                    Checklist(col, data.IncludedDocuments);
                });

                page.Footer().AlignRight().Text(
                    $"Generat de RIDElance · {data.GeneratedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Ro)}")
                    .FontSize(9).FontColor(Colors.Grey.Medium);
            });
        }).GeneratePdf();

        return DossierAssembler.Assemble(cover, data.IncludedDocuments, data.IsTest);
    }

    public byte[] GenerateVehicleDossier(VehicleDossierData data)
    {
        byte[] cover = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Grey.Darken4));

                page.Header().Column(header =>
                {
                    header.Item().Text("RIDElance").Bold().FontSize(18).FontColor(Colors.Blue.Darken2);
                    header.Item().Text("Dosar copie conformă & ecusoane").SemiBold().FontSize(13);
                });

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Column(info =>
                    {
                        info.Spacing(4);
                        Row(info, "Solicitant", data.ApplicantName);
                        if (!string.IsNullOrWhiteSpace(data.LegalName))
                        {
                            Row(info, "Denumire PFA", data.LegalName!);
                        }
                        if (!string.IsNullOrWhiteSpace(data.Cui))
                        {
                            Row(info, "CUI", data.Cui!);
                        }
                        if (!string.IsNullOrWhiteSpace(data.PlateNumber))
                        {
                            Row(info, "Nr. înmatriculare", data.PlateNumber!);
                        }
                        if (!string.IsNullOrWhiteSpace(data.Vin))
                        {
                            Row(info, "Serie șasiu (VIN)", data.Vin!);
                        }
                        if (!string.IsNullOrWhiteSpace(data.VehicleDescription))
                        {
                            Row(info, "Vehicul", data.VehicleDescription!);
                        }
                    });

                    col.Item().PaddingTop(8).Text("Copie conformă").SemiBold().FontSize(12);
                    col.Item().Column(copy =>
                    {
                        copy.Spacing(4);
                        Row(copy, "Perioadă", $"{data.CopyYears} an(i)");
                        Row(copy, "Taxă/an", $"{Lei(data.CopyFeePerYearBani)} lei");
                        Row(copy, "Total copie conformă", $"{Lei(data.CopyTotalFeeBani)} lei");
                    });

                    col.Item().PaddingTop(8).Text("Ecusoane").SemiBold().FontSize(12);
                    col.Item().Column(badges =>
                    {
                        badges.Spacing(3);
                        if (data.Badges.Count == 0)
                        {
                            badges.Item().Text("— fără ecusoane solicitate —").Italic().FontColor(Colors.Grey.Medium);
                        }
                        else
                        {
                            foreach (VehicleBadgeLine badge in data.Badges)
                            {
                                Row(badges, badge.Provider, $"{badge.SetCount} set(uri) · {Lei(badge.TotalBani)} lei");
                            }

                            Row(badges, "Total ecusoane", $"{Lei(data.BadgesTotalBani)} lei");
                        }
                    });

                    Checklist(col, data.IncludedDocuments);
                });

                page.Footer().AlignRight().Text(
                    $"Generat de RIDElance · {data.GeneratedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", Ro)}")
                    .FontSize(9).FontColor(Colors.Grey.Medium);
            });
        }).GeneratePdf();

        return DossierAssembler.Assemble(cover, data.IncludedDocuments, data.IsTest);
    }

    /// <summary>Cuprinsul de pe copertă: ce documente urmează, în ordinea în care sunt atașate.</summary>
    private static void Checklist(ColumnDescriptor col, IReadOnlyList<DossierAttachment> attachments)
    {
        col.Item().PaddingTop(8).Text("Documente incluse în dosar").SemiBold().FontSize(12);
        col.Item().Column(list =>
        {
            list.Spacing(3);
            if (attachments.Count == 0)
            {
                list.Item().Text("— niciun document atașat —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            foreach (DossierAttachment attachment in attachments)
            {
                list.Item().Text($"☑  {attachment.Label}");
            }
        });
    }

    private static string Lei(long bani) => (bani / 100m).ToString("N2", Ro);

    private static void Row(ColumnDescriptor col, string label, string value)
    {
        col.Item().Row(row =>
        {
            row.ConstantItem(140).Text(label).SemiBold();
            row.RelativeItem().Text(value);
        });
    }
}

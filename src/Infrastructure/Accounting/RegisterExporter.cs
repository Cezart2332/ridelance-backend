using System.Globalization;
using Application.Abstractions.Services;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Infrastructure.Accounting;

/// <summary>
/// Exportul registrelor contabile (spec contabilitate B7): PDF cu QuestPDF, cu layoutul apropiat de
/// modelul oficial (titlu, antet PFA și CUI, coloane numerotate, codul formularului), și Excel cu
/// ClosedXML, cu sumele ca numere.
/// </summary>
internal sealed class RegisterExporter : IRegisterExporter
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");

    public byte[] ToPdf(RegisterDocument document) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(document.Columns.Count > 4 ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(style => style.FontSize(8.5f));

                page.Header().Column(header =>
                {
                    header.Item().AlignCenter().Text(document.Title).Bold().FontSize(13);
                    foreach (string line in document.Header)
                    {
                        header.Item().AlignCenter().Text(line).FontSize(9);
                    }

                    header.Item().PaddingBottom(8);
                });

                page.Content().Column(content =>
                {
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (RegisterColumn column in document.Columns)
                            {
                                columns.RelativeColumn(column.Width);
                            }
                        });

                        table.Header(header =>
                        {
                            foreach (RegisterColumn column in document.Columns)
                            {
                                header.Cell().Element(HeaderCell).AlignCenter().AlignMiddle()
                                    .Text(column.Header).SemiBold();
                            }

                            foreach (string number in document.ColumnNumbers ?? [])
                            {
                                header.Cell().Element(Bordered).AlignCenter().Text(number).FontSize(7.5f);
                            }
                        });

                        foreach (RegisterLine line in document.Lines)
                        {
                            for (int index = 0; index < document.Columns.Count; index++)
                            {
                                object? value = index < line.Cells.Count ? line.Cells[index] : null;
                                IContainer cell = table.Cell().Element(Bordered);
                                cell = document.Columns[index].Numeric ? cell.AlignRight() : cell.AlignLeft();
                                TextBlockDescriptor text = cell.Text(Format(value));
                                if (line.Emphasis)
                                {
                                    text.Bold();
                                }
                            }
                        }
                    });

                    foreach (string note in document.Notes)
                    {
                        content.Item().PaddingTop(6).Text(note).FontSize(7.5f).Italic();
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(text =>
                    {
                        text.Span("Pagina ").FontSize(7.5f);
                        text.CurrentPageNumber().FontSize(7.5f);
                        text.Span(" din ").FontSize(7.5f);
                        text.TotalPages().FontSize(7.5f);
                    });
                    row.RelativeItem().AlignRight().Text(document.FormCode).FontSize(7.5f);
                });
            });
        }).GeneratePdf();

    public byte[] ToXlsx(RegisterDocument document)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.Worksheets.Add("Registru");
        int row = 1;
        sheet.Cell(row, 1).Value = document.Title;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 13;
        foreach (string line in document.Header)
        {
            sheet.Cell(++row, 1).Value = line;
        }

        row += 2;
        for (int column = 0; column < document.Columns.Count; column++)
        {
            IXLCell cell = sheet.Cell(row, column + 1);
            cell.Value = document.Columns[column].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        if (document.ColumnNumbers is { } numbers)
        {
            row++;
            for (int column = 0; column < numbers.Count; column++)
            {
                sheet.Cell(row, column + 1).Value = numbers[column];
                sheet.Cell(row, column + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
        }

        int firstData = row + 1;
        foreach (RegisterLine line in document.Lines)
        {
            row++;
            for (int column = 0; column < document.Columns.Count; column++)
            {
                object? value = column < line.Cells.Count ? line.Cells[column] : null;
                IXLCell cell = sheet.Cell(row, column + 1);
                switch (value)
                {
                    case decimal amount:
                        cell.Value = amount;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        break;
                    case int number:
                        cell.Value = number;
                        break;
                    case null:
                        break;
                    default:
                        cell.Value = Format(value);
                        break;
                }

                cell.Style.Font.Bold = line.Emphasis;
            }
        }

        if (row >= firstData - 1)
        {
            sheet.Range(firstData - (document.ColumnNumbers is null ? 1 : 2), 1, row, document.Columns.Count).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            sheet.Range(firstData - (document.ColumnNumbers is null ? 1 : 2), 1, row, document.Columns.Count).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        foreach (string note in document.Notes)
        {
            row += 2;
            sheet.Cell(row, 1).Value = note;
        }

        sheet.Cell(row + 2, 1).Value = document.FormCode;
        for (int column = 0; column < document.Columns.Count; column++)
        {
            sheet.Column(column + 1).Width = Math.Clamp(document.Columns[column].Width * 14, 8, 60);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten3).PaddingVertical(2).PaddingHorizontal(3);

    private static IContainer Bordered(IContainer container) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Darken1).PaddingVertical(2).PaddingHorizontal(3);

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        decimal amount => amount.ToString("#,##0.00", Ro),
        IFormattable formattable => formattable.ToString(null, Ro),
        _ => value.ToString() ?? string.Empty,
    };
}

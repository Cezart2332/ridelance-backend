namespace Application.Abstractions.Services;

/// <summary>O coloană a unui registru; <see cref="Numeric"/> se aliniază la dreapta și se însumează ca număr în Excel.</summary>
public sealed record RegisterColumn(string Header, bool Numeric = false, float Width = 1);

/// <summary>Un rând: celulele în ordinea coloanelor. Sumele sunt <see cref="decimal"/>, restul text.</summary>
/// <param name="Emphasis">Rând de total (îngroșat).</param>
public sealed record RegisterLine(IReadOnlyList<object?> Cells, bool Emphasis = false);

/// <summary>
/// Un registru contabil de exportat (spec contabilitate B7), în forma modelului oficial: titlul,
/// codul formularului (<c>14-1-1/b</c>), antetul PFA-ului, coloanele numerotate ca în model, rândurile.
/// </summary>
/// <param name="Header">Liniile de sub titlu: PFA, CUI, perioada, statusul.</param>
/// <param name="ColumnNumbers">Rândul cu numerele coloanelor din model (<c>0, 1, 2 …</c>), dacă modelul îl are.</param>
/// <param name="Notes">Mențiuni sub tabel.</param>
public sealed record RegisterDocument(
    string Title,
    string FormCode,
    IReadOnlyList<string> Header,
    IReadOnlyList<RegisterColumn> Columns,
    IReadOnlyList<string>? ColumnNumbers,
    IReadOnlyList<RegisterLine> Lines,
    IReadOnlyList<string> Notes);

/// <summary>Exportul registrelor: PDF (QuestPDF) și Excel (ClosedXML).</summary>
public interface IRegisterExporter
{
    byte[] ToPdf(RegisterDocument document);

    byte[] ToXlsx(RegisterDocument document);
}

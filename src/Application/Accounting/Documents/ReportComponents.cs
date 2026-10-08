using Application.Accounting.Contracts;

namespace Application.Accounting.Documents;

/// <summary>
/// Componentele rezumatului lunar Bolt, citite separat și verificate fiecare în textul PDF-ului.
/// Stau în <see cref="ExtractedFields.OtherAmounts"/>, sub etichetele de aici.
/// </summary>
/// <remarks>
/// Venitul brut din raport e doar TOTAL-ul de la „Defalcare tarif” (cursele, cu numerar, taxe de
/// anulare și rezervare, bacșiș) — cifra pe care o arată și raportul. „Alte venituri” (bonusuri,
/// compensări) nu intră în brut din raport: se adaugă abia când decontul din bancă arată că au fost
/// încasate (<c>PlatformLedgerSource</c>). Comisionul nu se scade din brut: e o cheltuială separată,
/// pe factura platformei.
/// </remarks>
public static class ReportComponents
{
    /// <summary>„DEFALCARE TARIF (INC. TVA) — TOTAL”.</summary>
    public const string FareTotal = "Total tarif curse";

    /// <summary>„DEFALCARE ALTE VENITURI — TOTAL”: bonusuri, compensări.</summary>
    public const string OtherIncomeTotal = "Total alte venituri";

    /// <summary>„Rambursări clienți”: bani dați înapoi pasagerilor, scăzuți din decontul șoferului.</summary>
    public const string CustomerRefunds = "Rambursări clienți";

    public static decimal? Of(ExtractedFields fields, string label) =>
        fields.OtherAmounts.FirstOrDefault(other => other.Label.Equals(label, StringComparison.OrdinalIgnoreCase))?.Amount;

    /// <summary>
    /// Venitul brut din componente: TOTAL-ul de la „Defalcare tarif”. Null când raportul nu are
    /// componentele (Uber, cu „Venituri totale” citit direct).
    /// </summary>
    public static decimal? Gross(ExtractedFields fields) => Of(fields, FareTotal);

    /// <summary>„Alte venituri” (bonusuri, compensări), ca sumă pozitivă; 0 când raportul nu le arată.</summary>
    public static decimal OtherIncome(ExtractedFields fields) => Math.Abs(Of(fields, OtherIncomeTotal) ?? 0);

    /// <summary>Rambursările către clienți, ca sumă pozitivă; 0 când raportul nu le arată.</summary>
    public static decimal Refunds(ExtractedFields fields) => Math.Abs(Of(fields, CustomerRefunds) ?? 0);

    /// <summary>
    /// Câmpurile cu venitul brut luat din TOTAL-ul de tarif, dacă modelul nu l-a dat sau a dat altceva.
    /// Componenta bate totalul citit: ea apare literal în document.
    /// </summary>
    public static ExtractedFields WithGross(ExtractedFields fields) =>
        Gross(fields) is { } gross && fields.Amount != gross ? fields with { Amount = gross } : fields;
}

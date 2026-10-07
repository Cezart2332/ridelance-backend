using Application.Accounting.Contracts;

namespace Application.Accounting.Documents;

/// <summary>
/// Componentele raportului lunar al platformei din care se compune venitul brut.
///
/// Rezumatul lunar Bolt nu are un rând „Venituri totale”: brutul e în două rânduri TOTAL, la
/// „Defalcare tarif” și la „Defalcare alte venituri”. Modelul nu are voie să adune, așa că le citește
/// separat, iar suma o face codul. Componentele stau în <see cref="ExtractedFields.OtherAmounts"/>,
/// sub etichetele de aici, ca fiecare să poată fi verificată în textul PDF-ului.
/// </summary>
/// <remarks>
/// Venitul brut e „sumele încasate … din desfășurarea activității” (Codul fiscal, art. 68 alin. (2)
/// lit. a)): tarifele curselor (inclusiv numerarul, taxele de anulare și rezervare, bacșișul) plus
/// celelalte venituri din platformă (bonusuri, compensări). Comisionul nu se scade din brut: e o
/// cheltuială separată, pe factura platformei.
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
    /// Venitul brut din componente: tarif + alte venituri. Null când raportul nu are componentele
    /// (Uber, cu „Venituri totale” citit direct).
    /// </summary>
    public static decimal? Gross(ExtractedFields fields) =>
        Of(fields, FareTotal) is { } fares ? fares + (Of(fields, OtherIncomeTotal) ?? 0) : null;

    /// <summary>Rambursările către clienți, ca sumă pozitivă; 0 când raportul nu le arată.</summary>
    public static decimal Refunds(ExtractedFields fields) => Math.Abs(Of(fields, CustomerRefunds) ?? 0);

    /// <summary>
    /// Câmpurile cu venitul brut completat din componente, dacă modelul nu l-a dat sau a dat altceva
    /// decât suma lor. Componentele bat totalul citit: ele apar literal în document.
    /// </summary>
    public static ExtractedFields WithGross(ExtractedFields fields) =>
        Gross(fields) is { } gross && fields.Amount != gross ? fields with { Amount = gross } : fields;
}

namespace Domain.Companies;

/// <summary>
/// Culorile mini-site-ului firmei.
/// </summary>
/// <remarks>
/// Control complet, nu o paletă închisă: firma își pune culoarea ei de brand, nu una dintre cele
/// zece pe care le-am ales noi. Riscul — o pagină ilizibilă — se acoperă în editor, unde
/// contrastul se calculează în timp real și se avertizează, nu prin a-i lua omului opțiunea.
///
/// Se salvează ca jsonb pe profil. Un tabel separat ar fi însemnat un join pentru fiecare
/// deschidere a paginii publice, ca să citim șapte valori care se schimbă o dată pe an.
/// </remarks>
public sealed class CompanyPageTheme
{
    /// <summary>Culoarea butoanelor, a liniilor de accent și a linkurilor.</summary>
    public string Accent { get; set; } = "#5CCBF5";

    /// <summary>Fundalul paginii.</summary>
    public string Background { get; set; } = "#FFFFFF";

    /// <summary>Fundalul cardurilor și al benzilor alternative.</summary>
    public string Surface { get; set; } = "#F6FCFE";

    public string Text { get; set; } = "#1A1A2E";

    /// <summary>Culoarea textului dinăuntrul butoanelor pline.</summary>
    public string ButtonText { get; set; } = "#FFFFFF";

    /// <summary>Vălul de peste fotografia de cover, ca titlul să rămână lizibil.</summary>
    public string HeroOverlay { get; set; } = "#0B1220";

    /// <summary>Cât de opac e vălul, în procente. 0..90 — 100 ar ascunde complet fotografia.</summary>
    public int HeroOverlayOpacity { get; set; } = 55;
}

/// <summary>Un avantaj afișat în secțiunea „De ce noi".</summary>
/// <remarks>
/// <see cref="IconKey"/> e o cheie dintr-o listă închisă, nu un nume liber de iconiță: altfel
/// frontendul ar trebui să se apere de orice text la randare. Lista trăiește în
/// <see cref="CompanyPageIcons"/> și e dublată în <c>src/components/company/highlightIcons.tsx</c>
/// — cele două trebuie ținute sincronizate.
/// </remarks>
public sealed class CompanyPageHighlight
{
    public string IconKey { get; set; } = "check";
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>Un rând din programul de lucru: „Luni – Vineri" / „09:00 – 18:00".</summary>
/// <remarks>
/// Zilele sunt text liber, nu o enumerație. O flotă scrie „Luni – Vineri", alta „În fiecare zi",
/// alta „Sâmbătă, doar cu programare" — o enumerație le-ar fi obligat pe toate la șapte rânduri.
/// </remarks>
public sealed class CompanyPageScheduleRow
{
    public string Day { get; set; } = string.Empty;
    public string Hours { get; set; } = string.Empty;
}

/// <summary>O întrebare frecventă, în varianta firmei.</summary>
public sealed class CompanyPageFaq
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// Conținutul secțiunilor proprii ale mini-site-ului.
/// </summary>
/// <remarks>
/// Nu există comutatoare „arată / ascunde secțiunea". O secțiune apare dacă are conținut, dispare
/// când se golește. Un set paralel de flag-uri ar fi însemnat două surse pentru aceeași întrebare
/// — și, inevitabil, o secțiune bifată „vizibilă" care nu are ce arăta.
/// </remarks>
public sealed class CompanyPageContent
{
    public List<CompanyPageHighlight> Highlights { get; set; } = [];
    public List<CompanyPageScheduleRow> Schedule { get; set; } = [];

    /// <summary>Orașele sau zonele în care se predă mașina.</summary>
    public List<string> CoverageAreas { get; set; } = [];

    /// <summary>O precizare sub zone: „Predare gratuită în București, restul cu tarif".</summary>
    public string? CoverageNote { get; set; }

    public List<CompanyPageFaq> Faq { get; set; } = [];
}

/// <summary>Cheile de iconiță acceptate pentru avantaje.</summary>
/// <remarks>
/// Sincronizată manual cu <c>src/components/company/highlightIcons.tsx</c>. O cheie adăugată doar
/// aici trece validarea și ajunge pe pagină ca iconiță implicită; una adăugată doar în frontend nu
/// poate fi salvată niciodată.
/// </remarks>
public static class CompanyPageIcons
{
    public const string Fallback = "check";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "check", "shield", "clock", "wallet", "car", "phone", "star", "wrench", "map", "bolt",
    };
}

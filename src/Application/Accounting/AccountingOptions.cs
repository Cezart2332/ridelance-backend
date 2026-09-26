namespace Application.Accounting;

/// <summary>Data la care e exigibil TVA-ul pe o factură de comision. DE CONFIRMAT (spec §6 pct. 3).</summary>
public enum VatExigibilityRule
{
    /// <summary>Data facturii.</summary>
    InvoiceDate = 0,

    /// <summary>Sfârșitul perioadei de serviciu facturate.</summary>
    ServicePeriodEnd = 1,
}

/// <summary>Ce curs se folosește pentru o factură în valută. DE CONFIRMAT (spec §6 pct. 4).</summary>
public enum ExchangeRateDateRule
{
    /// <summary>Ultimul curs publicat în ziua facturii sau înainte.</summary>
    SameDayOrPrevious = 0,

    /// <summary>Ultimul curs publicat strict înainte de ziua facturii.</summary>
    PreviousPublication = 1,
}

/// <summary>Rotunjirea la nivel de declarație. DE CONFIRMAT, per tip de declarație (spec §6 pct. 5).</summary>
public enum DeclarationRounding
{
    /// <summary>Două zecimale, ca liniile.</summary>
    None = 0,

    /// <summary>Lei întregi (rotunjire comercială).</summary>
    WholeLei = 1,
}

/// <summary>
/// Configurarea modulului de contabilitate (secțiunea <c>Accounting</c>). Pragurile verificărilor
/// și regulile DE CONFIRMAT stau aici, nu în cod (spec §0 pct. 3).
/// </summary>
public sealed class AccountingOptions
{
    public const string SectionName = "Accounting";

    /// <summary>Monedele acceptate de <c>CURRENCY_ALLOWED</c>.</summary>
    public IList<string> AllowedCurrencies { get; init; } = ["RON", "EUR"];

    /// <summary><c>SETTLEMENT_CORRELATION</c>: comisionul, ca procent din venit, e între aceste limite.</summary>
    public decimal SettlementMinPercent { get; init; } = 10;

    public decimal SettlementMaxPercent { get; init; } = 30;

    /// <summary>Regula pentru <c>PERIOD_MATCH</c> și cota de TVA. DE CONFIRMAT.</summary>
    public VatExigibilityRule VatExigibility { get; init; } = VatExigibilityRule.InvoiceDate;

    /// <summary>Regula zilei cursului valutar. DE CONFIRMAT.</summary>
    public ExchangeRateDateRule ExchangeRateDate { get; init; } = ExchangeRateDateRule.SameDayOrPrevious;

    /// <summary>
    /// Rotunjirea totalului, pe tip de declarație (<c>D100</c>, <c>D301</c>); lipsă = fără rotunjire.
    /// D100 pornește în lei întregi, fiindcă XSD-ul ANAF nu acceptă bani pentru sume (B4); metoda
    /// de rotunjire rămâne DE CONFIRMAT.
    /// </summary>
    public IDictionary<string, DeclarationRounding> DeclarationRounding { get; init; } = new Dictionary<string, DeclarationRounding>
    {
        ["D100"] = Accounting.DeclarationRounding.WholeLei,
    };

    /// <summary>Statele UE (cod TVA; Grecia e <c>EL</c>): serviciile de la furnizori de aici intră în D301 și D390.</summary>
    public IList<string> EuCountries { get; init; } =
    [
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES", "FI", "FR", "HR", "HU",
        "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "SE", "SI", "SK",
    ];

    /// <summary>Modelul pentru extracție; gol = modelul implicit OpenRouter.</summary>
    public string? ExtractionModel { get; init; }

    /// <summary>Versiunea promptului, salvată pe fiecare extracție.</summary>
    public string ExtractionPromptVersion { get; init; } = "accounting-extraction-v1";

    /// <summary>Dimensiunea maximă a unui PDF încărcat.</summary>
    public long MaxUploadBytes { get; init; } = 25 * 1024 * 1024;
}

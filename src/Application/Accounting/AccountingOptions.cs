namespace Application.Accounting;

/// <summary>Data la care e exigibil TVA-ul pe o factură de comision. DE CONFIRMAT (spec §6 pct. 3).</summary>
public enum VatExigibilityRule
{
    /// <summary>Data facturii.</summary>
    InvoiceDate = 0,

    /// <summary>Sfârșitul perioadei de serviciu facturate.</summary>
    ServicePeriodEnd = 1,
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

    /// <summary>Modelul pentru extracție; gol = modelul implicit OpenRouter.</summary>
    public string? ExtractionModel { get; init; }

    /// <summary>Versiunea promptului, salvată pe fiecare extracție.</summary>
    public string ExtractionPromptVersion { get; init; } = "accounting-extraction-v1";

    /// <summary>Dimensiunea maximă a unui PDF încărcat.</summary>
    public long MaxUploadBytes { get; init; } = 25 * 1024 * 1024;
}

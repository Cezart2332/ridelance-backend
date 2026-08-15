namespace Domain.Expenses;

/// <summary>De unde au venit datele cheltuielii.</summary>
public enum ExpenseSource
{
    /// <summary>Completată de om, integral.</summary>
    Manual = 0,

    /// <summary>Precompletată din documentul încărcat, apoi confirmată sau corectată de om.</summary>
    Ocr = 1,
}

/// <summary>
/// Cât de departe a ajuns cheltuiala în fluxul ei.
///
/// Doar cele confirmate intră în profitul real estimat. Verificarea documentului de către
/// admin rămâne un semnal separat, pe <c>Document.Status</c>: userul vede imediat efectul
/// cheltuielii, dar și faptul că suma nu e încă validată.
/// </summary>
public enum ExpenseStatus
{
    /// <summary>Salvată, dar încă needitată la capăt — nu intră în niciun calcul.</summary>
    Draft = 0,

    /// <summary>Confirmată de utilizator. Intră în profit.</summary>
    Confirmed = 1,
}

using Application.Abstractions.Anaf;
using Application.Accounting.Tax;
using Domain.Accounting;

namespace Application.Accounting.Declarations;

/// <summary>
/// Coloana <c>snapshot_json</c> a unei versiuni: intrările și calculul din momentul generării,
/// plus identificarea PFA-ului din antet (fără IBAN). Regulile schimbate ulterior nu o ating;
/// nivelul 1 de validare recalculează din ea.
/// </summary>
internal sealed record DeclarationSnapshot(PfaTaxInput Input, DeclarationCalculation Calculation, AnafTaxpayer? Taxpayer, DateTime CalculatedAtUtc)
{
    public static DeclarationSnapshot? Read(string json)
    {
        DeclarationSnapshot? snapshot = AccountingJson.Deserialize<DeclarationSnapshot?>(json, null);
        return snapshot?.Input is null || snapshot.Calculation is null ? null : snapshot;
    }
}

/// <summary>Istoricul de status al unei versiuni (<c>status_history_json</c>).</summary>
internal static class DeclarationStatusHistory
{
    /// <summary>Trece versiunea în <paramref name="to"/> și notează tranziția.</summary>
    public static void Move(DeclarationVersion version, DeclarationStatus to, Guid? userId, string? note)
    {
        List<Months.StatusHistoryRecord> history = AccountingJson.Deserialize<List<Months.StatusHistoryRecord>>(version.StatusHistoryJson, []);
        history.Add(new Months.StatusHistoryRecord(version.Status, to, DateTime.UtcNow, userId, note));
        version.StatusHistoryJson = AccountingJson.Serialize(history);
        version.Status = to;
    }
}

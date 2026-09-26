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

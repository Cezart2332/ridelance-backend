using Application.Accounting.Contracts;
using Domain.Accounting;

namespace Application.Abstractions.Anaf;

/// <summary>
/// Datele de identificare ale PFA-ului din antetul declarației. Declarantul e titularul (spec §6
/// pct. 11: semnarea ca împuternicit e DE CONFIRMAT).
/// </summary>
/// <param name="Iban">Contul (D301). Nu se păstrează în snapshot, doar în XML-ul criptat.</param>
public sealed record AnafTaxpayer(
    string Cui,
    string Name,
    string Address,
    string DeclarantLastName,
    string DeclarantFirstName,
    string DeclarantFunction,
    string? BankName,
    string? Iban);

/// <summary>O linie a declarației, cu documentul din care vine (D301: numărul și data facturii).</summary>
public sealed record AnafDeclarationLine(
    string? DocumentNumber,
    DateOnly? DocumentDate,
    decimal AmountInCurrency,
    string Currency,
    decimal? ExchangeRate,
    decimal Base,
    decimal Value,
    string SupplierName,
    string SupplierCountry,
    string SupplierVatId);

/// <summary>Tot ce intră în XML-ul unei versiuni de declarație.</summary>
/// <param name="Amount">Totalul versiunii (de plată; D390: 0).</param>
public sealed record AnafDeclarationInput(
    DeclarationType Type,
    string Period,
    bool Rectificative,
    AnafTaxpayer Taxpayer,
    IReadOnlyList<AnafDeclarationLine> Lines,
    decimal Amount);

/// <summary>
/// XML-ul declarațiilor ANAF (spec contabilitate B4): un mapper per declarație și per versiune de
/// schemă, plus validarea XSD. Versiunea schemei vine din <see cref="AnafDeclarationSchema"/>,
/// aleasă după perioada declarației.
/// </summary>
public interface IDeclarationXmlService
{
    bool Supports(DeclarationType type, string schemaVersion);

    /// <summary>XML-ul, UTF-8. Aruncă pentru o schemă fără mapper (verifică întâi <see cref="Supports"/>).</summary>
    byte[] Build(string schemaVersion, AnafDeclarationInput input);

    /// <summary>
    /// Nivelul 1, partea de XML: citește XML-ul înapoi și îl compară cu datele (sume, linii,
    /// sume de control ANAF). Listă goală = conform.
    /// </summary>
    IReadOnlyList<ValidationMessage> VerifyContent(string schemaVersion, AnafDeclarationInput input, byte[] xml);

    /// <summary>Nivelul 2: validarea cu XSD-ul oficial. Erorile sunt puse pe câmp.</summary>
    IReadOnlyList<ValidationMessage> ValidateSchema(string xsdPath, byte[] xml);
}

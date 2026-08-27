namespace Application.Abstractions.Dossiers;

/// <summary>Un rând etichetă-valoare dintr-un document de închiriere.</summary>
public sealed record RentalDocumentField(string Label, string? Value);

/// <summary>O secțiune din document: un titlu și rândurile lui.</summary>
public sealed record RentalDocumentSection(string Title, IReadOnlyList<RentalDocumentField> Fields);

/// <param name="Title">Titlul de pe prima pagină: „Contract de închiriere".</param>
/// <param name="PublicCode">Codul închirierii, tipărit ca număr de document.</param>
/// <param name="Sections">Părțile, obiectul, condițiile — în ordinea în care se citesc.</param>
/// <param name="Clauses">Textul de condiții, dacă firma și-a setat unul.</param>
/// <param name="SignatureLines">Cine semnează. Două linii la contract, două la proces-verbal.</param>
public sealed record RentalDocumentData(
    string Title,
    string PublicCode,
    IReadOnlyList<RentalDocumentSection> Sections,
    string? Clauses,
    IReadOnlyList<string> SignatureLines,
    DateTime GeneratedAtUtc);

/// <summary>
/// Produce PDF-urile unei închirieri: contract și procese-verbale.
/// </summary>
/// <remarks>
/// Primește date deja compuse, nu entități. Generatorul nu trebuie să știe ce e un `Rental` — altfel
/// fiecare câmp nou din domeniu ar fi cerut o modificare în stratul de tipărire.
/// </remarks>
public interface IRentalDocumentGenerator
{
    byte[] Generate(RentalDocumentData data);
}

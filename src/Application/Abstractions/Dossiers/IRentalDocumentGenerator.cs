namespace Application.Abstractions.Dossiers;

/// <summary>Un rând etichetă-valoare dintr-un document de închiriere.</summary>
public sealed record RentalDocumentField(string Label, string? Value);

/// <summary>O secțiune din document: un titlu și rândurile lui.</summary>
public sealed record RentalDocumentSection(string Title, IReadOnlyList<RentalDocumentField> Fields);

/// <param name="Pdf">Documentul tipărit.</param>
/// <param name="Source">
/// Sursa din care a ieșit, opacă pentru apelant. Se păstrează pentru că e singurul mod de a
/// retipări mai târziu exact același document, cu semnătura pe el.
/// </param>
public sealed record RentalDocumentOutput(byte[] Pdf, string Source);

/// <param name="Image">Semnătura, ca PNG.</param>
/// <param name="Note">
/// Mențiunea de sub nume: când și cum s-a semnat. O semnătură tipărită fără ea nu spune dacă a fost
/// dată pe hârtie sau printr-un link, și nici când.
/// </param>
public sealed record RentalSignature(byte[] Image, string Note);

/// <param name="Title">Titlul de pe prima pagină: „Contract de închiriere".</param>
/// <param name="PublicCode">Codul închirierii, tipărit ca număr de document.</param>
/// <param name="Sections">Părțile, obiectul, condițiile — în ordinea în care se citesc.</param>
/// <param name="Clauses">Textul de condiții, dacă firma și-a setat unul.</param>
/// <param name="SignatureLines">
/// Cine semnează, în ordinea liniilor de pe document. Poziția din listă, plus unu, e numărul liniei
/// pe care se așază mai târziu semnătura.
/// </param>
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
/// <para>
/// Tipărirea e asincronă pentru că se face în afara procesului: PDF-ul iese dintr-un motor LaTeX
/// pornit ca proces separat, iar un fir de execuție blocat câteva sute de milisecunde per document
/// e un fir pe care nu-l mai are cine să-l folosească la cereri.
/// </para>
/// </remarks>
public interface IRentalDocumentGenerator
{
    /// <param name="signatures">
    /// Semnăturile de pus din prima tipărire — în practică, specimenul firmei. Sursa întoarsă nu le
    /// conține, ca retipărirea de la semnare să pornească de la același text.
    /// </param>
    Task<RentalDocumentOutput> GenerateAsync(
        RentalDocumentData data,
        IReadOnlyDictionary<int, RentalSignature> signatures,
        CancellationToken cancellationToken = default);

    /// <summary>Retipărește un document deja generat, cu semnăturile date pe liniile lui.</summary>
    /// <param name="source">Sursa întoarsă la generare, păstrată de atunci.</param>
    /// <param name="signatures">Semnătura, pe numărul liniei pe care se așază.</param>
    /// <remarks>
    /// Se pornește de la sursa păstrată, nu de la date recompuse: între generare și semnare se pot
    /// schimba chiriașul, mașina sau termenii, iar documentul semnat trebuie să rămână documentul
    /// care a fost citit și semnat, nu unul refăcut din datele de azi.
    /// </remarks>
    Task<byte[]> SignAsync(
        string source,
        IReadOnlyDictionary<int, RentalSignature> signatures,
        CancellationToken cancellationToken = default);
}

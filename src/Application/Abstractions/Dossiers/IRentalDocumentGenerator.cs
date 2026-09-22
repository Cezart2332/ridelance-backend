namespace Application.Abstractions.Dossiers;

/// <summary>Un rând etichetă-valoare dintr-un document de închiriere.</summary>
public sealed record RentalDocumentField(string Label, string? Value);

/// <summary>O secțiune din document: un titlu și rândurile lui.</summary>
public sealed record RentalDocumentSection(string Title, IReadOnlyList<RentalDocumentField> Fields);

/// <summary>Ce fel de bucată dintr-un paragraf de contract.</summary>
public enum RentalTextKind
{
    /// <summary>Textul șablonului, tipărit ca atare.</summary>
    Text,

    /// <summary>O valoare completată din datele firmei, ale chiriașului sau ale închirierii.</summary>
    Value,

    /// <summary>Un loc gol, de completat de mână. <c>Text</c> e lățimea liniei (ex. „3cm”).</summary>
    Blank,

    /// <summary>O căsuță de bifat. <c>Text</c> e „x” când e bifată, gol altfel.</summary>
    Checkbox,
}

/// <summary>O bucată de text dintr-un paragraf.</summary>
public sealed record RentalTextRun(string Text, RentalTextKind Kind = RentalTextKind.Text);

/// <summary>Semnăturile de la finalul unui articol: rolul fiecărei părți și numele ei.</summary>
public sealed record RentalSignatureBlock(IReadOnlyList<string> Captions, IReadOnlyList<string> Names);

/// <summary>
/// Un capitol din textul contractului („I. PĂRȚILE CONTRACTANTE”) și paragrafele lui, fiecare
/// alcătuit din bucăți de text și de valori completate.
/// </summary>
/// <param name="Title">Titlul capitolului, centrat. Lipsă pentru paragrafele fără titlu.</param>
/// <param name="NewPage">Capitolul începe pe o pagină nouă (anexa).</param>
/// <param name="Heading">Un titlu mare de document, pentru anexa din același PDF.</param>
/// <param name="Signatures">Semnăturile care încheie capitolul, acolo unde le are șablonul.</param>
public sealed record RentalDocumentArticle(
    string? Title,
    IReadOnlyList<IReadOnlyList<RentalTextRun>> Paragraphs,
    bool NewPage = false,
    string? Heading = null,
    RentalSignatureBlock? Signatures = null);

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
/// <param name="Articles">
/// Textul documentului, după șablonul contractului de închiriere, completat. Când există, se tipărește
/// el în locul secțiunilor de câmpuri.
/// </param>
/// <param name="Kicker">Rândul mic de deasupra titlului: „ANEXA NR. 1”.</param>
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
    DateTime GeneratedAtUtc,
    IReadOnlyList<RentalDocumentArticle>? Articles = null,
    string? Kicker = null);

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

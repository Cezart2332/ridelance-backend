namespace Application.Abstractions.Dossiers;

/// <summary>
/// Datele împuternicirii ANAF, gata de tipărit. Un câmp null iese în PDF ca „—”: documentul se
/// generează oricum, iar ce lipsește apare la admin.
/// </summary>
public sealed record AnafMandateData(
    string Number,
    DateOnly Date,
    AnafMandant Mandant,
    AnafMandatar Mandatar);

/// <summary>Titularul PFA-ului, care dă împuternicirea.</summary>
public sealed record AnafMandant(
    string? FullName,
    string? Cnp,
    string? Domicile,
    string? IdSeries,
    string? IdNumber,
    string? IdIssuer,
    DateOnly? IdIssuedOn,
    string? PfaName,
    string? ProfessionalOffice,
    string? Cui,
    string? RegistryNumber);

/// <summary>Omul nostru, care primește împuternicirea. Vine din configurarea serverului.</summary>
public sealed record AnafMandatar(
    string? FullName,
    string? Cnp,
    string? Domicile,
    string? IdSeries,
    string? IdNumber,
    string? Email);

/// <summary>Împuternicirea ANAF, cu textul din modelul RIDElance.</summary>
public interface IAnafMandatePdfGenerator
{
    byte[] Generate(AnafMandateData data);
}

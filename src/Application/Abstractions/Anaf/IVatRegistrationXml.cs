namespace Application.Abstractions.Anaf;

/// <summary>Ce intră în D700: luna cererii, PFA-ul și titularul care o semnează.</summary>
/// <param name="Period">Luna cererii, <c>yyyy-MM</c>.</param>
public sealed record D700Content(
    string Period,
    string Cui,
    string Name,
    string DeclarantLastName,
    string DeclarantFirstName,
    string DeclarantFunction,
    D700Representative? Representative = null);

/// <summary>
/// Secțiunea III din D700, „Reprezentare prin împuternicit”: cine depune declarația în numele
/// PFA-ului și actul care îi dă dreptul.
/// </summary>
/// <param name="ActNumber">Numărul împuternicirii (cel mult 10 caractere în declarație).</param>
/// <param name="Cif">CNP-ul împuternicitului.</param>
public sealed record D700Representative(
    string ActNumber,
    DateOnly ActDate,
    string Name,
    string Cif,
    string Address,
    string? Email,
    string? Phone);

/// <summary>
/// XML-ul D700 (<c>mfp:anaf:dgti:d700:declaratie:v4</c>) pentru un PFA care cere cod de TVA
/// art. 317: declarație de mențiuni (felD 2), tip 070, secțiunea B.VI.
/// </summary>
public interface IVatRegistrationXml
{
    /// <summary>Codul declarației în kitul ANAF, pentru validator.</summary>
    const string DeclarationType = "D700";

    byte[] Build(D700Content content);
}

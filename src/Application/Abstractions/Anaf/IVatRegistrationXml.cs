namespace Application.Abstractions.Anaf;

/// <summary>Ce intră în D700: luna cererii, PFA-ul și titularul care o semnează.</summary>
/// <param name="Period">Luna cererii, <c>yyyy-MM</c>.</param>
public sealed record D700Content(
    string Period,
    string Cui,
    string Name,
    string DeclarantLastName,
    string DeclarantFirstName,
    string DeclarantFunction);

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

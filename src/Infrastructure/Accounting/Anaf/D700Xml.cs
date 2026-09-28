using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Application.Abstractions.Anaf;

namespace Infrastructure.Accounting.Anaf;

/// <summary>
/// D700 pentru codul de TVA art. 317 al unui PFA care primește servicii de la Uber și Bolt
/// (structura XML D700, OPANAF 15/2026, validator J5.0.3):
/// <list type="bullet">
/// <item><c>felD = 2</c>, declarație de mențiuni: PFA-ul are deja CUI;</item>
/// <item><c>dec_inreg = 070</c>, PF care desfășoară activități economice în mod independent;</item>
/// <item>secțiunea B.VI, pct. 1 „Înregistrare”, litera a) persoană impozabilă neînregistrată conform art. 316;</item>
/// <item>rândul 1.23.1, opțiunea 3: primirea de servicii de la un prestator stabilit în alt stat membru,
/// pentru care beneficiarul datorează TVA în România (art. 307 alin. 2), adică exact comisioanele
/// Uber (NL) și Bolt (EE).</item>
/// </list>
/// Fără reprezentant (secțiunea III) și fără atașament: declarantul e titularul, ca la D100.
/// <c>data_decl</c> apare în structură, dar validatorul îl respinge ca atribut necunoscut.
/// </summary>
internal sealed class D700Xml : IVatRegistrationXml
{
    public const string Namespace = "mfp:anaf:dgti:d700:declaratie:v4";

    public byte[] Build(D700Content content)
    {
        ArgumentNullException.ThrowIfNull(content);
        (int month, int year) = AnafFormat.Period(content.Period);
        XNamespace ns = Namespace;

        var root = new XElement(
            ns + "D700",
            Attribute("an", year),
            Attribute("luna", month),
            new XAttribute("nume_decl", Limit(AnafFormat.Text(content.DeclarantLastName), 75)),
            new XAttribute("pren_decl", Limit(AnafFormat.Text(content.DeclarantFirstName), 75)),
            new XAttribute("func_decl", Limit(AnafFormat.Text(content.DeclarantFunction), 50)),
            // Suma de control: Bifa_A + … + Bifa_G. Doar secțiunea B e bifată.
            Attribute("totalPlata_A", 1),
            Attribute("felD", 2),
            new XAttribute("cif", content.Cui),
            new XAttribute("den", Limit(AnafFormat.Text(content.Name), 200)),
            new XAttribute("dec_inreg", "070"),
            Attribute("Bifa_III", 0),
            Attribute("Bifa_A", 0),
            Attribute("Bifa_B", 1),
            Attribute("Bifa_C", 0),
            Attribute("Bifa_D", 0),
            Attribute("Bifa_E", 0),
            Attribute("Bifa_F", 0),
            Attribute("Bifa_G", 0),
            Attribute("Bifa_B1", 0),
            Attribute("Bifa_B2", 0),
            Attribute("Bifa_B3", 0),
            Attribute("Bifa_B4", 0),
            Attribute("Bifa_B5", 0),
            Attribute("Bifa_B6", 1),
            Attribute("Bifa_B7", 0),
            Attribute("Bifa_B8", 0),
            Attribute("Bifa_6b", 1),
            Attribute("Bifa_6b_abc", 1),
            Attribute("Bifa1_6b", 1),
            Attribute("Bifa_6b_inreg", 3));

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(writer);
        }

        return stream.ToArray();
    }

    private static XAttribute Attribute(string name, int value) => new(name, value.ToString(CultureInfo.InvariantCulture));

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length].TrimEnd();
}

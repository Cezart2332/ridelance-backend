using System.Collections.Concurrent;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Infrastructure.Accounting.Anaf;

/// <summary>
/// O corecție a unui XSD oficial, aplicată în memorie la încărcare. Fișierul din repo rămâne cel
/// descărcat de pe site-ul ANAF; corecția acoperă o nepotrivire dovedită între XSD și structura
/// oficială a declarației (anexa cu validări), pe care validatorul ANAF (DUKIntegrator) o respectă.
/// </summary>
internal sealed record AnafSchemaCorrection(string XsdPath, string Description, Action<XElement> Apply);

/// <summary>
/// Corecțiile cunoscute. Fiecare are un test care arată că, fără ea, un XML acceptat de DUKIntegrator
/// ar pica XSD-ul. Când ANAF publică un XSD corect, corecția devine inutilă (nu mai găsește ce să schimbe).
/// </summary>
internal static class AnafSchemaCorrections
{
    public const string D100V2 = "Anaf/Schemas/D100/v2-20220224/d100_24022022.xsd.xml";
    public const string D301V1 = "Anaf/Schemas/D301/v1-20200130/d301_20200130.xsd.xml";
    public const string D390V3 = "Anaf/Schemas/D390/v3-20210212/d390_12022021.xsd.xml";

    private static readonly XNamespace Xs = XmlSchema.Namespace;

    public static readonly IReadOnlyList<AnafSchemaCorrection> All =
    [
        new(
            D100V2,
            "cod_oblig 634 (impozit pe comisioanele nerezidenților): în structura D100 (poz. 16) și în validatorul D100, lipsă din enumerarea XSD-ului.",
            schema => AddEnumeration(schema, "Int_listaCodObligSType", "634")),
        new(
            D301V1,
            "temei = 0 („nu s-a bifat”, declarație inițială): permis de structura D301 (nr. crt. 5b), XSD-ul cere 1–2.",
            schema =>
            {
                XElement? temei = Named(schema, "attribute", "temei");
                if (temei is null || (string?)temei.Attribute("type") != "IntInt1_2SType")
                {
                    return;
                }

                temei.SetAttributeValue("type", "IntInt0_2SType");
                schema.Add(new XElement(
                    Xs + "simpleType",
                    new XAttribute("name", "IntInt0_2SType"),
                    new XElement(
                        Xs + "restriction",
                        new XAttribute("base", "xs:integer"),
                        new XElement(Xs + "minInclusive", new XAttribute("value", "0")),
                        new XElement(Xs + "maxInclusive", new XAttribute("value", "2")))));
            }),
        new(
            D390V3,
            "Secțiunea <cos> are 0–n apariții în structura D390 (OPANAF 705/2020); XSD-ul o cere cel puțin o dată.",
            schema => Named(schema, "element", "cos")?.SetAttributeValue("minOccurs", "0")),
    ];

    private static XElement? Named(XElement schema, string kind, string name) =>
        schema.Descendants(Xs + kind).FirstOrDefault(element => (string?)element.Attribute("name") == name);

    private static void AddEnumeration(XElement schema, string simpleType, string value)
    {
        XElement? restriction = Named(schema, "simpleType", simpleType)?.Element(Xs + "restriction");
        if (restriction is not null && !restriction.Elements(Xs + "enumeration").Any(e => (string?)e.Attribute("value") == value))
        {
            restriction.Add(new XElement(Xs + "enumeration", new XAttribute("value", value)));
        }
    }
}

/// <summary>
/// XSD-urile oficiale, încorporate în assembly (<c>Accounting/Anaf/Schemas/{TIP}/{versiune}/</c>),
/// compilate o singură dată, cu corecțiile din <see cref="AnafSchemaCorrections"/>.
/// </summary>
internal static class AnafSchemas
{
    private static readonly ConcurrentDictionary<(string Path, bool Corrected), XmlSchemaSet> Cache = new();

    /// <summary>Căile relative (<c>Anaf/Schemas/…</c>) ale XSD-urilor din assembly.</summary>
    public static IReadOnlyList<string> Paths => [.. Resources.Keys];

    public static bool Exists(string xsdPath) => Resources.ContainsKey(Normalize(xsdPath));

    /// <summary>Schema compilată; <paramref name="corrected"/> = <c>false</c> doar pentru testele corecțiilor.</summary>
    public static XmlSchemaSet Load(string xsdPath, bool corrected = true) =>
        Cache.GetOrAdd((Normalize(xsdPath), corrected), key =>
        {
            if (!Resources.TryGetValue(key.Path, out string? resource))
            {
                throw new FileNotFoundException($"XSD-ul {key.Path} nu există în assembly.", key.Path);
            }

            XElement schema;
            using (Stream stream = typeof(AnafSchemas).Assembly.GetManifestResourceStream(resource)!)
            {
                schema = XElement.Load(stream);
            }

            if (key.Corrected)
            {
                foreach (AnafSchemaCorrection correction in AnafSchemaCorrections.All.Where(c => c.XsdPath == key.Path))
                {
                    correction.Apply(schema);
                }
            }

            var set = new XmlSchemaSet { XmlResolver = null };
            using (XmlReader reader = schema.CreateReader())
            {
                set.Add(XmlSchema.Read(reader, null)!);
            }

            set.Compile();
            return set;
        });

    private static readonly Dictionary<string, string> Resources = Assembly.GetExecutingAssembly()
        .GetManifestResourceNames()
        .Where(name => name.StartsWith("Anaf/Schemas/", StringComparison.Ordinal) || name.StartsWith("Anaf\\Schemas\\", StringComparison.Ordinal))
        .ToDictionary(Normalize, name => name, StringComparer.Ordinal);

    private static string Normalize(string path) => path.Replace('\\', '/');
}

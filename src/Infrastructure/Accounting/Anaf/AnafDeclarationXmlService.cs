using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Application.Abstractions.Anaf;
using Application.Accounting.Contracts;
using Domain.Accounting;

namespace Infrastructure.Accounting.Anaf;

/// <summary>
/// Generarea XML-ului (mapperul schemei perioadei) și nivelul 2 de validare, cu XSD-ul oficial
/// compilat de <see cref="AnafSchemas"/>.
/// </summary>
internal sealed class AnafDeclarationXmlService : IDeclarationXmlService
{
    private static readonly IDeclarationXmlMapper[] Mappers = [new D100MapperV2(), new D301MapperV1(), new D390MapperV3()];

    public bool Supports(DeclarationType type, string schemaVersion) => Find(type, schemaVersion) is not null;

    public byte[] Build(string schemaVersion, AnafDeclarationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        IDeclarationXmlMapper mapper = Find(input.Type, schemaVersion)
            ?? throw new InvalidOperationException($"Nu există mapper pentru {input.Type} {schemaVersion}.");
        return mapper.Map(input);
    }

    public IReadOnlyList<ValidationMessage> VerifyContent(string schemaVersion, AnafDeclarationInput input, byte[] xml)
    {
        ArgumentNullException.ThrowIfNull(input);
        IDeclarationXmlMapper? mapper = Find(input.Type, schemaVersion);
        return mapper is null
            ? [new ValidationMessage(null, $"Nu există mapper pentru {input.Type} {schemaVersion}.")]
            : mapper.Verify(input, xml);
    }

    public IReadOnlyList<ValidationMessage> ValidateSchema(string xsdPath, byte[] xml)
    {
        if (!AnafSchemas.Exists(xsdPath))
        {
            return [new ValidationMessage(null, $"XSD-ul {xsdPath} nu există în aplicație.")];
        }

        return Validate(AnafSchemas.Load(xsdPath), xml);
    }

    /// <summary>Validează XML-ul cu o schemă compilată; fiecare eroare are câmpul (atributul sau elementul) ei.</summary>
    internal static List<ValidationMessage> Validate(XmlSchemaSet schemas, byte[] xml)
    {
        var messages = new List<ValidationMessage>();
        XDocument document;
        try
        {
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            return [new ValidationMessage(null, $"XML-ul nu e bine format: {exception.Message}")];
        }

        document.Validate(schemas, (sender, args) =>
        {
            string? field = sender switch
            {
                XAttribute attribute => attribute.Name.LocalName,
                XElement element => element.Name.LocalName,
                _ => null,
            };
            string line = args.Exception?.LineNumber > 0
                ? string.Create(CultureInfo.InvariantCulture, $" (linia {args.Exception.LineNumber})")
                : string.Empty;
            messages.Add(new ValidationMessage(field, $"{(args.Severity == XmlSeverityType.Warning ? "Atenționare: " : string.Empty)}{args.Message}{line}"));
        });
        return messages;
    }

    private static IDeclarationXmlMapper? Find(DeclarationType type, string schemaVersion) =>
        Mappers.FirstOrDefault(mapper => mapper.Type == type && mapper.SchemaVersion == schemaVersion);
}

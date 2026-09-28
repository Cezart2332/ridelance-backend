using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Application.Accounting.Anaf;

/// <summary>Ce citim din factura e-Factura (UBL 2.1 / CIUS-RO).</summary>
internal sealed record EFacturaInvoice(
    bool CreditNote,
    string? Number,
    DateOnly? IssueDate,
    string? SupplierName,
    string? SupplierCif,
    string? CustomerName,
    string? CustomerCif,
    string? Currency,
    decimal? Total,
    decimal? Vat);

/// <summary>
/// Arhiva descărcată din e-Factura: factura (<c>{id_incarcare}.xml</c>) și semnătura Ministerului
/// Finanțelor (<c>semnatura_….xml</c>). La mesajele de eroare, XML-ul e lista erorilor, nu o factură.
/// </summary>
internal static class EFacturaXml
{
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    /// <summary>XML-ul principal din arhivă (nu semnătura); <c>null</c> dacă arhiva nu are unul.</summary>
    public static byte[]? MainXml(byte[] zip)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
                e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                !e.Name.StartsWith("semnatura", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }

            using Stream stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Datele facturii; <c>null</c> dacă XML-ul nu e o factură sau o notă de credit UBL.</summary>
    public static EFacturaInvoice? Read(byte[] xml)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        XElement? root = document.Root;
        if (root is null || root.Name.LocalName is not ("Invoice" or "CreditNote"))
        {
            return null;
        }

        string? currency = Value(root.Element(Cbc + "DocumentCurrencyCode"));
        XElement? supplier = root.Element(Cac + "AccountingSupplierParty")?.Element(Cac + "Party");
        XElement? customer = root.Element(Cac + "AccountingCustomerParty")?.Element(Cac + "Party");
        XElement? totals = root.Element(Cac + "LegalMonetaryTotal");
        XElement? vat = root.Elements(Cac + "TaxTotal")
            .Select(total => total.Element(Cbc + "TaxAmount"))
            .FirstOrDefault(amount => amount is not null && (currency is null || (string?)amount.Attribute("currencyID") == currency));

        return new EFacturaInvoice(
            root.Name.LocalName == "CreditNote",
            Value(root.Element(Cbc + "ID")),
            DateOnly.TryParseExact(Value(root.Element(Cbc + "IssueDate")), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly issued) ? issued : null,
            PartyName(supplier),
            PartyCif(supplier),
            PartyName(customer),
            PartyCif(customer),
            currency,
            Amount(totals?.Element(Cbc + "PayableAmount")) ?? Amount(totals?.Element(Cbc + "TaxInclusiveAmount")),
            Amount(vat));
    }

    private static string? PartyName(XElement? party) =>
        Value(party?.Element(Cac + "PartyLegalEntity")?.Element(Cbc + "RegistrationName"))
        ?? Value(party?.Element(Cac + "PartyName")?.Element(Cbc + "Name"));

    private static string? PartyCif(XElement? party) =>
        Value(party?.Element(Cac + "PartyTaxScheme")?.Element(Cbc + "CompanyID"))
        ?? Value(party?.Element(Cac + "PartyLegalEntity")?.Element(Cbc + "CompanyID"));

    private static string? Value(XElement? element) =>
        string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    private static decimal? Amount(XElement? element) =>
        decimal.TryParse(element?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : null;
}

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace Infrastructure.Accounting.Anaf;

/// <summary>Regulile de format comune declarațiilor ANAF (din structurile oficiale ale fișierelor XML).</summary>
internal static class AnafFormat
{
    /// <summary><c>ZZ.LL.AAAA</c></summary>
    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    public static (int Month, int Year) Period(string period)
    {
        var start = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (start.Month, start.Year);
    }

    /// <summary>Scadența D100 / D301: 25 a lunii următoare perioadei de raportare.</summary>
    public static DateOnly DueDate(string period)
    {
        (int month, int year) = Period(period);
        return new DateOnly(year, month, 25).AddMonths(1);
    }

    /// <summary>
    /// Numărul de evidență a plății (23 de cifre): primele 21 de poziții, plus suma lor de control
    /// (ultimele două cifre ale sumei cifrelor).
    /// </summary>
    public static string EvidenceNumber(string first21)
    {
        if (first21.Length != 21 || !first21.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Numărul de evidență are 21 de cifre înainte de suma de control.", nameof(first21));
        }

        int sum = first21.Sum(digit => digit - '0');
        return first21 + (sum % 100).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Poz. 8–17 ale numărului de evidență: <c>LLAA</c> perioada și <c>ZZLLAA</c> scadența.</summary>
    public static string PeriodAndDue(string period)
    {
        (int month, int year) = Period(period);
        DateOnly due = DueDate(period);
        return string.Create(CultureInfo.InvariantCulture, $"{month:00}{year % 100:00}{due.Day:00}{due.Month:00}{due.Year % 100:00}");
    }

    public static bool IsValidEvidenceNumber(string value) =>
        value.Length == 23 && value.All(char.IsAsciiDigit) && EvidenceNumber(value[..21]) == value;

    /// <summary>
    /// Text fără diacritice: PDF-ul DUKIntegrator nu le afișează („București” → „Bucure?ti”).
    /// Orice alt caracter în afara ASCII devine spațiu.
    /// </summary>
    public static string Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c switch
            {
                'ß' => "ss",
                'Ø' => "O",
                'ø' => "o",
                'Ł' => "L",
                'ł' => "l",
                'Đ' => "D",
                'đ' => "d",
                _ when c is >= ' ' and <= '~' => c.ToString(),
                _ => " ",
            });
        }

        return Collapse(builder.ToString());
    }

    /// <summary>
    /// Denumirea unui operator D390: caracterele permise sunt litere, cifre, spațiu și <c>+ - . @</c>
    /// (structura D390).
    /// </summary>
    public static string OperatorName(string? value) =>
        Collapse(new string([.. Text(value).Select(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '+' or '-' or '.' or '@' ? c : ' ')]));

    /// <summary>Codul de TVA fără prefixul de țară (<c>EE102090374</c> → <c>102090374</c>).</summary>
    public static string VatNumber(string? vatId)
    {
        string compact = new([.. (vatId ?? string.Empty).Where(c => !char.IsWhiteSpace(c))]);
        return compact.Length > 2 && char.IsAsciiLetter(compact[0]) && char.IsAsciiLetter(compact[1]) ? compact[2..] : compact;
    }

    public static byte[] Serialize<T>(T declaration, string xmlNamespace)
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add(string.Empty, xmlNamespace);
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            new XmlSerializer(typeof(T)).Serialize(writer, declaration, namespaces);
        }

        return stream.ToArray();
    }

    /// <summary>Citește XML-ul înapoi (fără DTD și entități externe); <c>null</c> dacă nu se poate.</summary>
    public static T? Deserialize<T>(byte[] xml)
        where T : class
    {
        try
        {
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return new XmlSerializer(typeof(T)).Deserialize(reader) as T;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string Collapse(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

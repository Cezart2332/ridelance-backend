namespace Application.Payments;

internal static class ServiceOrderConfirmationEmail
{
    public static string Subject => "Confirmare plată — RIDElance";

    public static string BuildHtml(
        string customerName,
        string serviceTitle,
        decimal amountLei)
    {
        string amountFormatted = amountLei.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("ro-RO"));
        string safeName = System.Net.WebUtility.HtmlEncode(customerName);
        string safeTitle = System.Net.WebUtility.HtmlEncode(serviceTitle);

        return $"""
            <div style="font-family: Arial, sans-serif; color: #1a1a1a; line-height: 1.6; max-width: 560px;">
              <p>Bună ziua, <strong>{safeName}</strong>,</p>
              <p>Îți confirmăm că am înregistrat plata pentru serviciul <strong>{safeTitle}</strong>, în valoare de <strong>{amountFormatted} lei</strong>.</p>
              <p>Echipa RIDElance te va contacta în curând pentru următorii pași. Dacă ai întrebări, răspunde la acest email sau scrie-ne la <a href="mailto:contact@ridelance.ro">contact@ridelance.ro</a>.</p>
              <p style="margin-top: 24px;">Cu stimă,<br/><strong>Echipa RIDElance</strong></p>
            </div>
            """;
    }
}

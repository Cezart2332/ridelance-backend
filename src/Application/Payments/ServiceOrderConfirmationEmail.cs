namespace Application.Payments;

internal static class ServiceOrderConfirmationEmail
{
    public static string Subject => "Confirmare plată — RIDElance";

    public static string BuildMjml(string customerName, string serviceTitle, decimal amountLei)
    {
        string amountFormatted = amountLei.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("ro-RO"));
        string safeName = System.Net.WebUtility.HtmlEncode(customerName);
        string safeTitle = System.Net.WebUtility.HtmlEncode(serviceTitle);
        string subject = Subject;

        return $@"
<mjml>
  <mj-head>
    <mj-title>{subject}</mj-title>
    <mj-font name=""Inter"" href=""https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap"" />
    <mj-attributes>
      <mj-all font-family=""Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif"" />
      <mj-text font-size=""16px"" color=""#374151"" line-height=""26px"" />
      <mj-section padding=""0"" />
    </mj-attributes>
  </mj-head>
  <mj-body background-color=""#F9FAFB"">
    <mj-spacer height=""40px"" />

    <mj-section padding=""0 20px"">
      <mj-column>
        <mj-text font-size=""24px"" font-weight=""800"" color=""#111827"" align=""center"">
          RIDE<span style=""color: #5CCBF5;"">lance</span>
        </mj-text>
      </mj-column>
    </mj-section>

    <mj-spacer height=""30px"" />

    <mj-section padding=""0 20px"">
      <mj-column background-color=""#ffffff"" padding=""30px"" border-radius=""12px"">
        <mj-text font-size=""28px"" font-weight=""700"" color=""#111827"" align=""center"" padding-bottom=""30px"">
          Plată confirmată
        </mj-text>

        <mj-text>
          Bună ziua, <strong>{safeName}</strong>,
        </mj-text>

        <mj-text>
          Îți confirmăm că am înregistrat plata pentru serviciul comandat pe <strong>RIDElance</strong>. Mai jos găsești detaliile comenzii:
        </mj-text>

        <mj-spacer height=""20px"" />

        <mj-table background-color=""#F9FAFB"" padding=""20px"">
          <tr>
            <td style=""padding-bottom: 5px; color: #6B7280; font-size: 14px;"">Serviciu</td>
          </tr>
          <tr>
            <td style=""padding-bottom: 15px; color: #111827; font-weight: 600;"">{safeTitle}</td>
          </tr>
          <tr>
            <td style=""padding-bottom: 5px; color: #6B7280; font-size: 14px;"">Sumă plătită</td>
          </tr>
          <tr>
            <td style=""color: #5CCBF5; font-weight: 700; font-size: 20px;"">{amountFormatted} lei</td>
          </tr>
        </mj-table>

        <mj-spacer height=""20px"" />

        <mj-text>
          Echipa RIDElance te va contacta în curând pentru următorii pași.
        </mj-text>

        <mj-spacer height=""30px"" />

        <mj-divider border-width=""1px"" border-color=""#F3F4F6"" />

        <mj-spacer height=""20px"" />

        <mj-text font-size=""14px"" color=""#6B7280"" line-height=""22px"">
          Ai întrebări? Răspunde la acest email sau scrie-ne la
          <a href=""mailto:contact@ridelance.ro"" style=""color: #45B8E2; font-weight: 600; text-decoration: none;"">contact@ridelance.ro</a>.
        </mj-text>

        <mj-spacer height=""10px"" />

        <mj-text>
          Cu stimă,<br />
          <strong>Echipa RIDElance</strong>
        </mj-text>
      </mj-column>
    </mj-section>

    <mj-section padding=""20px"">
      <mj-column>
        <mj-text align=""center"" font-size=""13px"" color=""#9CA3AF"">
          &copy; {DateTime.UtcNow.Year} RIDElance Digital Solutions. Toate drepturile rezervate.
        </mj-text>
      </mj-column>
    </mj-section>

    <mj-spacer height=""40px"" />
  </mj-body>
</mjml>";
    }
}

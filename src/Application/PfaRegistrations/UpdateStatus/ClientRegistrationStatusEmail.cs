using System.Net;

namespace Application.PfaRegistrations.UpdateStatus;

internal static class ClientRegistrationStatusEmail
{
    public const string ApprovedSubject = "Felicitări! PFA-ul tău a fost aprobat — RIDElance";
    public const string RejectedSubject = "Dosarul tău PFA necesită modificări — RIDElance";

    public static string BuildApprovedMjml(string customerName, string cui)
    {
        string safeName = WebUtility.HtmlEncode(customerName);
        string safeCui = WebUtility.HtmlEncode(cui);
        string subject = ApprovedSubject;

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
        <mj-text font-size=""24px"" font-weight=""700"" color=""#16a34a"" align=""center"" padding-bottom=""20px"">
          Dosar PFA Aprobat
        </mj-text>

        <mj-text>
          Bună ziua, <strong>{safeName}</strong>,
        </mj-text>

        <mj-text>
          Avem vești excelente! Dosarul tău pentru deschiderea PFA a fost verificat și aprobat cu succes de către contabilul tău dedicat.
        </mj-text>

        <mj-spacer height=""10px"" />

        <mj-table background-color=""#F0FDF4"" padding=""20px"" style=""border: 1px solid #bbf7d0; border-radius: 8px;"">
          <tr>
            <td style=""padding-bottom: 5px; color: #16a34a; font-size: 13px; font-weight: 700; text-transform: uppercase;"">Codul tău de înregistrare (CUI)</td>
          </tr>
          <tr>
            <td style=""color: #111827; font-weight: 800; font-size: 24px; letter-spacing: 1px;"">{safeCui}</td>
          </tr>
        </mj-table>

        <mj-spacer height=""20px"" />

        <mj-text>
          Certificatul tău de înregistrare a fost încărcat în platformă. Te poți loga oricând în contul tău pentru a accesa documentele oficiale și a începe activitatea de ridesharing pe propriul PFA.
        </mj-text>

        <mj-spacer height=""25px"" />

        <mj-button href=""https://ridelance.ro/auth"" background-color=""#5CCBF5"" color=""white"" font-size=""16px"" font-weight=""700"" border-radius=""8px"" padding=""10px 24px"">
          Intră în cont
        </mj-button>

        <mj-spacer height=""30px"" />

        <mj-divider border-width=""1px"" border-color=""#F3F4F6"" />

        <mj-spacer height=""20px"" />

        <mj-text font-size=""14px"" color=""#6B7280"" line-height=""22px"">
          Ai întrebări? Contactează contabilul direct din chat-ul din aplicație sau scrie-ne la
          <a href=""mailto:contact@ridelance.ro"" style=""color: #45B8E2; font-weight: 600; text-decoration: none;"">contact@ridelance.ro</a>.
        </mj-text>

        <mj-spacer height=""10px"" />

        <mj-text>
          Cu drag,<br />
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

    public static string BuildRejectedMjml(string customerName, string reviewNote)
    {
        string safeName = WebUtility.HtmlEncode(customerName);
        string safeNote = WebUtility.HtmlEncode(reviewNote);
        string subject = RejectedSubject;

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
        <mj-text font-size=""24px"" font-weight=""700"" color=""#dc2626"" align=""center"" padding-bottom=""20px"">
          Dosar PFA Neconform
        </mj-text>

        <mj-text>
          Bună ziua, <strong>{safeName}</strong>,
        </mj-text>

        <mj-text>
          Te informăm că dosarul tău pentru deschiderea PFA a fost verificat și necesită atenție sau corecturi suplimentare.
        </mj-text>

        <mj-spacer height=""10px"" />

        <mj-table background-color=""#FEF2F2"" padding=""20px"" style=""border: 1px solid #fecaca; border-radius: 8px;"">
          <tr>
            <td style=""padding-bottom: 5px; color: #dc2626; font-size: 13px; font-weight: 700; text-transform: uppercase;"">Mențiunile contabilului</td>
          </tr>
          <tr>
            <td style=""color: #111827; font-weight: 500; font-size: 15px; line-height: 22px;"">{safeNote}</td>
          </tr>
        </mj-table>

        <mj-spacer height=""20px"" />

        <mj-text>
          Te rugăm să te loghezi în contul tău RIDElance pentru a vedea detaliile complete, a lua legătura cu contabilul pe chat sau pentru a reîncărca documentele corecte.
        </mj-text>

        <mj-spacer height=""25px"" />

        <mj-button href=""https://ridelance.ro/auth"" background-color=""#dc2626"" color=""white"" font-size=""16px"" font-weight=""700"" border-radius=""8px"" padding=""10px 24px"">
          Remediază dosarul
        </mj-button>

        <mj-spacer height=""30px"" />

        <mj-divider border-width=""1px"" border-color=""#F3F4F6"" />

        <mj-spacer height=""20px"" />

        <mj-text font-size=""14px"" color=""#6B7280"" line-height=""22px"">
          Dacă ai neclarități, trimite un mesaj pe chat direct din aplicație sau scrie-ne la
          <a href=""mailto:contact@ridelance.ro"" style=""color: #45B8E2; font-weight: 600; text-decoration: none;"">contact@ridelance.ro</a>.
        </mj-text>

        <mj-spacer height=""10px"" />

        <mj-text>
          Cu respect,<br />
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

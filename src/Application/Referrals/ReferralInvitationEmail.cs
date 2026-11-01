using System.Net;
using Application.Abstractions;
using SharedKernel;

namespace Application.Referrals;

/// <summary>Invitația unui prieten: cine îl invită, linkul de înscriere și codul, pentru cine îl scrie de mână.</summary>
internal static class ReferralInvitationEmail
{
    public static Task<Result> SendAsync(
        IEmailService emailService,
        IMjmlRenderer mjmlRenderer,
        string to,
        string inviterName,
        string code,
        string link,
        CancellationToken cancellationToken)
    {
        string name = WebUtility.HtmlEncode(inviterName);
        string href = WebUtility.HtmlEncode(link);
        string subject = $"{inviterName} te invită în RIDElance";
        string mjml = $@"
<mjml>
  <mj-head>
    <mj-attributes>
      <mj-all font-family=""Helvetica, Arial, sans-serif"" />
      <mj-text font-size=""15px"" color=""#374151"" line-height=""24px"" />
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
    <mj-spacer height=""24px"" />
    <mj-section padding=""0 20px"">
      <mj-column background-color=""#ffffff"" padding=""28px"" border-radius=""12px"">
        <mj-text><strong>{name}</strong> te invită în RIDElance, platforma PFA pentru șoferii de ridesharing.</mj-text>
        <mj-spacer height=""12px"" />
        <mj-button background-color=""#111827"" color=""#ffffff"" font-size=""15px"" font-weight=""700"" href=""{href}"" border-radius=""8px"" inner-padding=""14px 32px"">
          Creează cont
        </mj-button>
        <mj-spacer height=""12px"" />
        <mj-text font-size=""13px"" color=""#6B7280"" align=""center"">Cod de recomandare: <strong>{WebUtility.HtmlEncode(code)}</strong></mj-text>
      </mj-column>
    </mj-section>
    <mj-spacer height=""40px"" />
  </mj-body>
</mjml>";

        return emailService.SendEmailAsync(to, subject, mjmlRenderer.Render(mjml), cancellationToken);
    }
}

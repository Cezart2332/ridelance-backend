using System.Net;
using Application.Abstractions;
using Domain.Users;
using SharedKernel;

namespace Application.Users.Staff;

/// <summary>Emailul invitației în echipă: doar linkul, fără parolă; parola și 2FA le setează omul.</summary>
internal static class StaffInvitationEmail
{
    public static Task<Result> SendAsync(
        IEmailService emailService,
        IMjmlRenderer mjmlRenderer,
        StaffInvitation invitation,
        string link,
        CancellationToken cancellationToken)
    {
        string role = invitation.Role == UserRole.Admin ? "administrator" : "contabil";
        string name = WebUtility.HtmlEncode(invitation.FullName);
        string href = WebUtility.HtmlEncode(link);
        string subject = "Invitație în echipa RIDElance";
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
        <mj-text>Salut, <strong>{name}</strong>,</mj-text>
        <mj-text>Ai fost invitat în echipa RIDElance ca <strong>{role}</strong>.</mj-text>
        <mj-text>Îți alegi parola și configurezi autentificarea în doi pași (Authy, Google Authenticator sau altă aplicație).</mj-text>
        <mj-spacer height=""12px"" />
        <mj-button background-color=""#111827"" color=""#ffffff"" font-size=""15px"" font-weight=""700"" href=""{href}"" border-radius=""8px"" inner-padding=""14px 32px"">
          Creează contul
        </mj-button>
        <mj-spacer height=""12px"" />
        <mj-text font-size=""13px"" color=""#6B7280"">Linkul expiră pe {invitation.ExpiresAtUtc:dd.MM.yyyy HH:mm} (UTC) și poate fi folosit o singură dată.</mj-text>
      </mj-column>
    </mj-section>
    <mj-spacer height=""40px"" />
  </mj-body>
</mjml>";

        return emailService.SendEmailAsync(invitation.Email, subject, mjmlRenderer.Render(mjml), cancellationToken);
    }
}

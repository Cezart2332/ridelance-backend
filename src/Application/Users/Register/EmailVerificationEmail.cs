using Application.Abstractions;
using Domain.Users;
using SharedKernel;

namespace Application.Users.Register;

/// <summary>
/// Emailul cu codul de confirmare.
/// </summary>
/// <remarks>
/// Șablonul e aici, nu în fiecare apelant: se trimite din două locuri — la înregistrare și la
/// retrimitere — iar două copii ar fi ajuns să difere.
/// </remarks>
internal static class EmailVerificationEmail
{
    public static Task<Result> SendAsync(
        IEmailService emailService,
        IMjmlRenderer mjmlRenderer,
        string email,
        string firstName,
        string code,
        CancellationToken cancellationToken)
    {
        string greeting = string.IsNullOrWhiteSpace(firstName) ? "Salut" : $"Salut, {firstName.Trim()}";
        int minutes = (int)EmailVerification.CodeLifetime.TotalMinutes;

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

    <mj-spacer height=""30px"" />

    <mj-section padding=""0 20px"">
      <mj-column background-color=""#ffffff"" padding=""30px"" border-radius=""12px"">
        <mj-text font-size=""26px"" font-weight=""700"" color=""#111827"" align=""center"" padding-bottom=""24px"">
          Confirmă-ți adresa de email
        </mj-text>

        <mj-text>
          {greeting},
        </mj-text>

        <mj-text>
          Ți-ai creat un cont pe <strong>RIDElance</strong>. Introdu codul de mai jos ca să confirmi
          că adresa aceasta îți aparține.
        </mj-text>

        <mj-spacer height=""20px"" />

        <mj-text align=""center"" font-size=""34px"" font-weight=""800"" color=""#111827"" letter-spacing=""10px"">
          {code}
        </mj-text>

        <mj-spacer height=""20px"" />

        <mj-text font-size=""14px"" color=""#6B7280"" align=""center"">
          Codul e valabil {minutes} de minute.
        </mj-text>

        <mj-spacer height=""24px"" />

        <mj-divider border-width=""1px"" border-color=""#F3F4F6"" />

        <mj-spacer height=""16px"" />

        <mj-text font-size=""14px"" color=""#6B7280"" line-height=""22px"">
          Dacă nu tu ai creat contul, ignoră mesajul. Fără cod, adresa nu e confirmată.
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

        return emailService.SendEmailAsync(
            email,
            "Codul tău de confirmare RIDElance",
            mjmlRenderer.Render(mjml),
            cancellationToken);
    }
}

using Application.Abstractions;
using SharedKernel;

namespace Application.Users.PasswordReset;

/// <summary>Emailul cu codul de resetare a parolei, cu același aspect ca cel de confirmare.</summary>
internal static class PasswordResetEmail
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
        int minutes = (int)Domain.Users.PasswordReset.CodeLifetime.TotalMinutes;

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
          Resetează-ți parola
        </mj-text>

        <mj-text>
          {greeting},
        </mj-text>

        <mj-text>
          Am primit o cerere de resetare a parolei contului tău <strong>RIDElance</strong>. Introdu
          codul de mai jos pe pagina de resetare și alege o parolă nouă.
        </mj-text>

        <mj-spacer height=""20px"" />

        <mj-text align=""center"" font-size=""34px"" font-weight=""800"" color=""#111827"" letter-spacing=""10px"">
          {code}
        </mj-text>

        <mj-spacer height=""20px"" />

        <mj-text font-size=""14px"" color=""#6B7280"" align=""center"">
          Codul e valabil {minutes} minute.
        </mj-text>

        <mj-spacer height=""24px"" />

        <mj-divider border-width=""1px"" border-color=""#F3F4F6"" />

        <mj-spacer height=""16px"" />

        <mj-text font-size=""14px"" color=""#6B7280"" line-height=""22px"">
          Dacă nu tu ai cerut resetarea, ignoră mesajul: parola rămâne cea de acum.
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
            "Codul de resetare a parolei RIDElance",
            mjmlRenderer.Render(mjml),
            cancellationToken);
    }
}

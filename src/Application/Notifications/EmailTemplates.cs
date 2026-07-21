using System.Net;

namespace Application.Notifications;

/// <summary>
/// Șabloane MJML partajate pentru emailurile tranzacționale (stilul RIDElance).
/// Randarea în HTML se face de apelant prin IMjmlRenderer.
/// </summary>
public static class EmailTemplates
{
    public static string Notice(
        string title,
        string? greetingName,
        IReadOnlyList<string> paragraphs,
        string? highlight,
        string ctaLabel,
        Uri ctaUrl)
    {
        string greeting = string.IsNullOrWhiteSpace(greetingName)
            ? string.Empty
            : $@"<mj-text>Salutare, <strong>{WebUtility.HtmlEncode(greetingName)}</strong>,</mj-text>";

        string body = string.Join("\n", paragraphs.Select(p => $"<mj-text>{WebUtility.HtmlEncode(p)}</mj-text>"));

        string highlightBlock = string.IsNullOrWhiteSpace(highlight)
            ? string.Empty
            : $@"
        <mj-spacer height=""10px"" />
        <mj-text container-background-color=""#FEF2F2"" color=""#991B1B"" padding=""16px 20px"" border-radius=""8px"">
          {WebUtility.HtmlEncode(highlight)}
        </mj-text>
        <mj-spacer height=""10px"" />";

        return $@"
<mjml>
  <mj-head>
    <mj-title>{WebUtility.HtmlEncode(title)}</mj-title>
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
        <mj-text font-size=""24px"" font-weight=""700"" color=""#111827"" align=""center"" padding-bottom=""24px"">
          {WebUtility.HtmlEncode(title)}
        </mj-text>

        {greeting}

        {body}

        {highlightBlock}

        <mj-spacer height=""20px"" />

        <mj-button background-color=""#5CCBF5"" color=""#ffffff"" font-size=""16px"" font-weight=""700"" href=""{ctaUrl}"" border-radius=""50px"" inner-padding=""16px 40px"" width=""100%"">
          {WebUtility.HtmlEncode(ctaLabel)}
        </mj-button>
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

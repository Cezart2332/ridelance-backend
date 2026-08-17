using System.Net;
using Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Application.PfaRegistrations.Onboarding.Notifications;

/// <summary>
/// Anunțuri interne despre onboarding, trimise pe adresa de operațiuni.
///
/// Expeditorul e mereu <c>contact@ridelance.ro</c> — e fixat în <c>ResendEmailService</c>, deci
/// nu se configurează aici. Destinatarul vine din <c>Notifications:OpsEmail</c>, ca să poată fi
/// schimbat fără deploy.
///
/// Toate metodele înghit erorile: un email nelivrat nu are voie să pice generarea unui dosar sau
/// procesarea unui webhook Stripe. Eșecul se logează, atât.
/// </summary>
public sealed class OnboardingOpsNotifier(
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    IConfiguration configuration,
    ILogger<OnboardingOpsNotifier> logger)
{
    private const string RecipientKey = "Notifications:OpsEmail";
    private const string DefaultRecipient = "cezarturliu25@gmail.com";

    private string Recipient => configuration[RecipientKey] is { Length: > 0 } configured
        ? configured
        : DefaultRecipient;

    /// <summary>Un dosar a fost generat și e gata de descărcat.</summary>
    public Task DossierGeneratedAsync(
        string dossierLabel,
        string applicantName,
        string? cui,
        string fileName,
        bool isTestSession,
        CancellationToken cancellationToken) =>
        SendAsync(
            $"Dosar generat: {dossierLabel}{(isTestSession ? " [TEST]" : string.Empty)}",
            $"S-a generat un dosar nou: {dossierLabel}.",
            [
                ("Solicitant", applicantName),
                ("CUI", string.IsNullOrWhiteSpace(cui) ? "—" : cui),
                ("Fișier", fileName),
                ("Sesiune de test", isTestSession ? "DA — dosarul are filigran TEST" : "nu"),
            ],
            cancellationToken);

    /// <summary>O plată a fost încasată.</summary>
    public Task PaymentReceivedAsync(
        string description,
        long amountBani,
        string customerEmail,
        string? stripeReference,
        CancellationToken cancellationToken) =>
        SendAsync(
            $"Plată încasată: {description}",
            $"A intrat o plată nouă: {description}.",
            [
                ("Sumă", $"{amountBani / 100m:N2} lei"),
                ("Client", customerEmail),
                ("Referință Stripe", string.IsNullOrWhiteSpace(stripeReference) ? "—" : stripeReference),
            ],
            cancellationToken);

    private async Task SendAsync(
        string subject,
        string headline,
        IReadOnlyList<(string Label, string Value)> rows,
        CancellationToken cancellationToken)
    {
        try
        {
            string html = mjmlRenderer.Render(BuildMjml(subject, headline, rows));
            await emailService.SendEmailAsync(Recipient, subject, html, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Un anunț intern ratat nu are voie să pice operațiunea care l-a declanșat.
            logger.LogWarning(exception, "Anunțul intern „{Subject}” nu a putut fi trimis.", subject);
        }
    }

    private static string BuildMjml(
        string subject,
        string headline,
        IReadOnlyList<(string Label, string Value)> rows)
    {
        string safeSubject = WebUtility.HtmlEncode(subject);
        string safeHeadline = WebUtility.HtmlEncode(headline);

        string tableRows = string.Join("\n", rows.Select(row => $@"
          <tr>
            <td style=""padding: 6px 12px 6px 0; color: #6B7280; font-size: 14px;"">{WebUtility.HtmlEncode(row.Label)}</td>
            <td style=""padding: 6px 0; color: #111827; font-size: 14px; font-weight: 600;"">{WebUtility.HtmlEncode(row.Value)}</td>
          </tr>"));

        return $@"
<mjml>
  <mj-head>
    <mj-title>{safeSubject}</mj-title>
    <mj-attributes>
      <mj-all font-family=""Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif"" />
      <mj-text font-size=""16px"" color=""#374151"" line-height=""26px"" />
      <mj-section padding=""0"" />
    </mj-attributes>
  </mj-head>
  <mj-body background-color=""#F9FAFB"">
    <mj-spacer height=""32px"" />
    <mj-section padding=""0 20px"">
      <mj-column background-color=""#FFFFFF"" border-radius=""12px"" padding=""28px"">
        <mj-text font-size=""13px"" color=""#6B7280"" letter-spacing=""1px"" text-transform=""uppercase"">
          RIDElance · notificare internă
        </mj-text>
        <mj-text font-size=""20px"" font-weight=""700"" color=""#111827"" padding-top=""8px"">
          {safeHeadline}
        </mj-text>
        <mj-table padding-top=""16px"">
          {tableRows}
        </mj-table>
      </mj-column>
    </mj-section>
    <mj-spacer height=""32px"" />
  </mj-body>
</mjml>";
    }
}

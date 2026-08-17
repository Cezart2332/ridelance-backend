using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions;
using Microsoft.Extensions.Logging;
using Resend;
using SharedKernel;

namespace Infrastructure.Email;

public class ResendEmailService(
    IResend resend,
    ILogger<ResendEmailService> logger) : IEmailService
{
    /// <summary>Expeditorul, același pentru tot ce pleacă din sistem.</summary>
    private const string From = "contact@ridelance.ro";

    public Task<Result> SendEmailAsync(
        string to,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default) =>
        SendEmailWithAttachmentsAsync(to, subject, htmlBody, [], cancellationToken);

    public async Task<Result> SendEmailWithAttachmentsAsync(
        string to,
        string subject,
        string htmlBody,
        IReadOnlyList<EmailAttachmentContent> attachments,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var message = new EmailMessage
            {
                From = From,
                To = { to },
                Subject = subject,
                HtmlBody = htmlBody,
            };

            foreach (EmailAttachmentContent attachment in attachments)
            {
                message.Attachments ??= [];
                message.Attachments.Add(new EmailAttachment
                {
                    Filename = attachment.FileName,
                    Content = attachment.Content,
                    ContentType = attachment.ContentType,
                });
            }

            ResendResponse<Guid> response = await resend.EmailSendAsync(message, cancellationToken);

            if (response.Content != Guid.Empty)
            {
                logger.LogInformation("Email sent successfully to {To} with ID: {EmailId}", to, response.Content);
                return Result.Success();
            }

            logger.LogError("Failed to send email to {To}. Response ID was empty.", to);
            return Result.Failure(Error.Failure("Email.SendFailed", "Nu s-a putut trimite emailul."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception occurred while sending email to {To}", to);
            return Result.Failure(Error.Failure("Email.SendFailed", "Nu s-a putut trimite emailul."));
        }
    }
}

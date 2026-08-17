using System.Threading;
using System.Threading.Tasks;
using SharedKernel;

namespace Application.Abstractions;

/// <summary>
/// Un fișier atașat unui email. <paramref name="Content"/> e conținutul brut, deja decriptat —
/// stratul de email nu știe nimic despre storage.
/// </summary>
public sealed record EmailAttachmentContent(string FileName, string ContentType, byte[] Content);

public interface IEmailService
{
    Task<Result> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// Același email, cu fișiere atașate. Există pentru pachetele care se trimit prin email ca
    /// atare — dosarul de înființare PFA, care ajunge la Consulto ca arhivă.
    /// </summary>
    Task<Result> SendEmailWithAttachmentsAsync(
        string to,
        string subject,
        string htmlBody,
        IReadOnlyList<EmailAttachmentContent> attachments,
        CancellationToken cancellationToken = default);
}

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
    /// <param name="highPriority">
    /// Marchează mesajul ca important. Clienții de email nu au un standard unic, deci se trimit
    /// toate cele trei antete uzuale (<c>X-Priority</c>, <c>Importance</c>,
    /// <c>X-MSMail-Priority</c>); Gmail se uită la <c>Importance</c>.
    /// </param>
    Task<Result> SendEmailWithAttachmentsAsync(
        string to,
        string subject,
        string htmlBody,
        IReadOnlyList<EmailAttachmentContent> attachments,
        bool highPriority = false,
        CancellationToken cancellationToken = default);
}

using System.Threading;
using System.Threading.Tasks;
using SharedKernel;

namespace Application.Abstractions;

public interface IEmailService
{
    Task<Result> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

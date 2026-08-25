using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using SharedKernel;

namespace Application.Invoicing.Commands.CancelInvoice;

public sealed record CancelInvoiceCommand(string SeriesName, string Number) : ICommand;

internal sealed class CancelInvoiceCommandHandler(
    IUserContext userContext,
    OwnerOblioResolver resolver,
    IOwnerInvoicingService invoicing)
    : ICommandHandler<CancelInvoiceCommand>
{
    public async Task<Result> Handle(CancelInvoiceCommand command, CancellationToken cancellationToken)
    {
        Result<OwnerOblioCredentials> credentials = await resolver.ResolveAsync(userContext.UserId, cancellationToken);
        if (credentials.IsFailure)
        {
            return Result.Failure(credentials.Error);
        }

        try
        {
            await invoicing.CancelAsync(credentials.Value, command.SeriesName, command.Number, cancellationToken);
        }
        catch (OblioApiException ex)
        {
            return Result.Failure(Error.Problem("Invoice.CancelFailed", ex.Message));
        }

        return Result.Success();
    }
}

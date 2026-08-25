using Application.Abstractions.Authentication;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using SharedKernel;

namespace Application.Invoicing.Commands.CollectInvoice;

/// <param name="PaymentMethod">„Ordin de plata", „Chitanta", „Card" — valorile din Oblio.</param>
public sealed record CollectInvoiceCommand(
    string SeriesName,
    string Number,
    long AmountBani,
    string PaymentMethod) : ICommand;

internal sealed class CollectInvoiceCommandHandler(
    IUserContext userContext,
    OwnerOblioResolver resolver,
    IOwnerInvoicingService invoicing)
    : ICommandHandler<CollectInvoiceCommand>
{
    public async Task<Result> Handle(CollectInvoiceCommand command, CancellationToken cancellationToken)
    {
        if (command.AmountBani <= 0)
        {
            return Result.Failure(Error.Problem("Invoice.InvalidAmount", "Suma încasată trebuie să fie pozitivă."));
        }

        Result<OwnerOblioCredentials> credentials = await resolver.ResolveAsync(userContext.UserId, cancellationToken);
        if (credentials.IsFailure)
        {
            return Result.Failure(credentials.Error);
        }

        try
        {
            // Încasarea se scrie direct în Oblio, nu local: acolo o vede și contabilul, iar o
            // stare locală ar fi divergat de prima încasare făcută din interfața lor.
            await invoicing.CollectAsync(
                credentials.Value,
                command.SeriesName,
                command.Number,
                command.AmountBani / 100m,
                command.PaymentMethod,
                cancellationToken);
        }
        catch (OblioApiException ex)
        {
            return Result.Failure(Error.Problem("Invoice.CollectFailed", ex.Message));
        }

        return Result.Success();
    }
}

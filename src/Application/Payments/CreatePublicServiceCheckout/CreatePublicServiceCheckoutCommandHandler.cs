using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Payments;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Payments.CreatePublicServiceCheckout;

internal sealed class CreatePublicServiceCheckoutCommandHandler(
    IApplicationDbContext context,
    IStripeService stripeService,
    IConfiguration configuration)
    : ICommandHandler<CreatePublicServiceCheckoutCommand, string>
{
    public async Task<Result<string>> Handle(
        CreatePublicServiceCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        if (!StripeCatalog.TryResolvePublicService(command.ServiceKey, out StripeCatalogItem? catalogItem, out string? title))
        {
            return Result.Failure<string>(Error.Problem("Service.InvalidKey", "Serviciul selectat nu este disponibil."));
        }

        string priceId = await stripeService.ResolvePriceIdAsync(catalogItem, cancellationToken);

#pragma warning disable S1075
        string baseUrl = configuration["App:BaseUrl"]
            ?? throw new InvalidOperationException("App:BaseUrl is missing in configuration.");
#pragma warning restore S1075

        string successUrl = command.SuccessUrl ?? $"{baseUrl}/?service_paid=1";
        string cancelUrl = command.CancelUrl ?? $"{baseUrl}/servicii";

        var order = new ServiceOrder
        {
            Id = Guid.NewGuid(),
            ServiceKey = command.ServiceKey,
            ServiceTitle = title,
            CustomerName = command.CustomerName.Trim(),
            CustomerEmail = command.CustomerEmail.Trim(),
            CustomerPhone = command.CustomerPhone.Trim(),
            Status = ServiceOrderStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        };

        context.ServiceOrders.Add(order);
        await context.SaveChangesAsync(cancellationToken);

        string sessionUrl = await stripeService.CreateCheckoutSessionAsync(
            priceId,
            "payment",
            successUrl,
            cancelUrl,
            order.CustomerEmail,
            userId: null,
            metadata: null,
            sessionMetadata: new Dictionary<string, string>
            {
                ["serviceOrderId"] = order.Id.ToString(),
            },
            cancellationToken: cancellationToken);

        return sessionUrl;
    }
}

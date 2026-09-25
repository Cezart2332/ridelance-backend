using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Payments.ServiceOrders;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using Domain.Payments;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Payments.CreatePublicServiceCheckout;

internal sealed class CreatePublicServiceCheckoutCommandHandler(
    IApplicationDbContext context,
    IStripeService stripeService,
    ServiceOrderDossierBuilder dossierBuilder,
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

        // Formularul întâi: o comandă fără datele cerute nu are voie să ajungă la plată — după
        // plată nu mai avem de la cine le cere.
        Result<ServiceOrderDossier> dossier = await dossierBuilder.BuildAsync(
            command.ServiceKey,
            command.Dossier,
            command.Context ?? new SignatureContext(null, null, null),
            cancellationToken);

        if (dossier.IsFailure)
        {
            return Result.Failure<string>(dossier.Error);
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
            UserId = command.UserId,
            DossierJson = dossier.Value.Serialize(),
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

using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Cars;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Payments.CreateCarListingCheckout;

internal sealed class CreateCarListingCheckoutCommandHandler(
    IStripeService stripeService,
    IApplicationDbContext context,
    IConfiguration configuration)
    : ICommandHandler<CreateCarListingCheckoutCommand, string>
{
    public async Task<Result<string>> Handle(
        CreateCarListingCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<string>(Error.NotFound("User.NotFound", "Utilizatorul nu a fost găsit."));
        }

        if (user.Role != UserRole.CarPoster)
        {
            return Result.Failure<string>(Error.Problem(
                "CarListingCheckout.Forbidden",
                "Plata pentru publicarea mașinilor este disponibilă doar pentru conturile de închirieri mașini."));
        }

        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<string>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (car.PostedByUserId != user.Id)
        {
            return Result.Failure<string>(Error.Problem(
                "Car.Forbidden",
                "Poți plăti doar pentru anunțurile tale."));
        }

        if (car.PaymentStatus == CarListingPaymentStatus.Paid)
        {
            return Result.Failure<string>(Error.Problem(
                "CarListingCheckout.AlreadyPaid",
                "Anunțul are deja plata activă."));
        }

        string priceId = await stripeService.ResolvePriceIdAsync(
            StripeCatalog.CarListingMonthly,
            cancellationToken: cancellationToken);

#pragma warning disable S1075 // URIs should not be hardcoded
        string baseUrl = configuration["App:BaseUrl"]
            ?? throw new InvalidOperationException("App:BaseUrl is missing in configuration.");
#pragma warning restore S1075

        string successUrl = command.SuccessUrl
            ?? $"{baseUrl}/poster?car_paid=1&car_id={car.Id}&session_id={{CHECKOUT_SESSION_ID}}";
        string cancelUrl = command.CancelUrl
            ?? $"{baseUrl}/poster?car_payment_cancelled=1&car_id={car.Id}";

        var sessionMetadata = new Dictionary<string, string>
        {
            ["paymentKind"] = "car_listing",
            ["carId"] = car.Id.ToString(),
            ["billingUnit"] = "posted_car",
        };

        string sessionUrl = await stripeService.CreateCheckoutSessionAsync(
            priceId,
            "subscription",
            successUrl,
            cancelUrl,
            user.Email,
            user.Id.ToString(),
            $"kind:car_listing|carId:{car.Id}",
            sessionMetadata,
            cancellationToken: cancellationToken);

        // Vizibilitatea nu se mai scrie aici: `Car.Active` o derivă din starea plății.
        car.PaymentStatus = CarListingPaymentStatus.Pending;
        car.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return sessionUrl;
    }
}

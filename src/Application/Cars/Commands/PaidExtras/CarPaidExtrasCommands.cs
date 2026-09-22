using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Cars;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Cars.Commands.PaidExtras;

/// <summary>
/// Plata unui anunț extra, peste cele incluse în abonamentul flotei: <see cref="Pricing.PaidExtras.ExtraListingMonthlyBani" />
/// pe lună, per mașină. Întoarce secretul pentru checkout-ul Stripe integrat în pagină.
/// </summary>
public sealed record CreateExtraListingCheckoutCommand(Guid CarId) : ICommand<string>;

/// <summary>
/// Ascunderea numărului de înmatriculare în anunț: <see cref="Pricing.PaidExtras.HiddenPlateBani" /> o
/// singură dată, per mașină. Rămâne plătită la orice republicare a aceleiași mașini.
/// </summary>
public sealed record CreateHiddenPlateCheckoutCommand(Guid CarId) : ICommand<string>;

/// <summary>Oprește abonamentul unui anunț extra. Anunțul iese din piață dacă nu mai are loc inclus.</summary>
public sealed record CancelExtraListingCommand(Guid CarId) : ICommand;

internal static class CarPaidExtras
{
    /// <summary>Descrierea plății de ascundere a numărului, citită și de privirea de ansamblu.</summary>
    public const string HiddenPlateDescriptionPrefix = "Ascundere număr înmatriculare";

    public const string HiddenPlatePaymentKind = "car_hidden_plate";

    /// <summary>
    /// Anunțul extra folosește fluxul plății per mașină deja existent (<c>car_listing</c>): abonament
    /// Stripe pe mașină, reînnoiri lunare, eșecuri și anulare — toate tratate deja de webhook.
    /// </summary>
    public const string ExtraListingPaymentKind = "car_listing";

    public static async Task<Result<(User Owner, Car Car)>> LoadOwnCarAsync(
        IApplicationDbContext context,
        IUserContext userContext,
        Guid carId,
        CancellationToken cancellationToken)
    {
        User? owner = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);
        Car? car = await context.Cars.SingleOrDefaultAsync(c => c.Id == carId, cancellationToken);

        if (owner is null || car is null || car.PostedByUserId != owner.Id)
        {
            return Result.Failure<(User, Car)>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (owner.Role != UserRole.CarPoster)
        {
            return Result.Failure<(User, Car)>(Error.Problem("Car.PaidExtrasFleetOnly", "Opțiunile plătite sunt pentru anunțurile firmelor."));
        }

        return (owner, car);
    }

    public static string ReturnUrl(IConfiguration configuration, Guid carId, string kind)
    {
#pragma warning disable S1075 // URIs should not be hardcoded
        string baseUrl = configuration["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl is missing in configuration.");
#pragma warning restore S1075
        return $"{baseUrl}/app/dashboard-srl/masini/{carId}?plata={kind}&session_id={{CHECKOUT_SESSION_ID}}";
    }
}

internal sealed class CreateExtraListingCheckoutCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IStripeService stripe,
    IConfiguration configuration)
    : ICommandHandler<CreateExtraListingCheckoutCommand, string>
{
    public async Task<Result<string>> Handle(CreateExtraListingCheckoutCommand command, CancellationToken cancellationToken)
    {
        Result<(User Owner, Car Car)> loaded = await CarPaidExtras.LoadOwnCarAsync(context, userContext, command.CarId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<string>(loaded.Error);
        }

        (User owner, Car car) = loaded.Value;

        if (car.ListingStatus == ListingStatus.Archived)
        {
            return Result.Failure<string>(Error.Problem("Car.Archived", "Anunțul e arhivat. Scoate-l din arhivă înainte să-l publici."));
        }

        if (car.PaymentStatus == CarListingPaymentStatus.Paid)
        {
            return Result.Failure<string>(Error.Conflict("Car.ExtraListingActive", "Mașina are deja un anunț extra activ."));
        }

        string priceId = await stripe.ResolvePriceIdAsync(StripeCatalog.ExtraListingMonthly, cancellationToken);
        string url = CarPaidExtras.ReturnUrl(configuration, car.Id, "anunt-extra");

        return await stripe.CreateCheckoutSessionAsync(
            priceId,
            "subscription",
            url,
            url,
            owner.Email,
            owner.Id.ToString(),
            "extra_listing",
            new Dictionary<string, string>
            {
                ["paymentKind"] = CarPaidExtras.ExtraListingPaymentKind,
                ["carId"] = car.Id.ToString(),
                ["stayOnPage"] = "true",
            },
            // Un dublu-click nu deschide două abonamente pentru aceeași mașină în aceeași zi.
            $"extra-listing:{car.Id}:{DateTime.UtcNow:yyyyMMdd}",
            null,
            cancellationToken);
    }
}

internal sealed class CreateHiddenPlateCheckoutCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IStripeService stripe,
    IConfiguration configuration)
    : ICommandHandler<CreateHiddenPlateCheckoutCommand, string>
{
    public async Task<Result<string>> Handle(CreateHiddenPlateCheckoutCommand command, CancellationToken cancellationToken)
    {
        Result<(User Owner, Car Car)> loaded = await CarPaidExtras.LoadOwnCarAsync(context, userContext, command.CarId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<string>(loaded.Error);
        }

        (User owner, Car car) = loaded.Value;

        if (car.PlateHidden)
        {
            return Result.Failure<string>(Error.Conflict("Car.PlateAlreadyHidden", "Numărul de înmatriculare e deja ascuns pentru mașina asta."));
        }

        string priceId = await stripe.ResolvePriceIdAsync(StripeCatalog.HiddenPlate, cancellationToken);
        string url = CarPaidExtras.ReturnUrl(configuration, car.Id, "numar-ascuns");

        return await stripe.CreateCheckoutSessionAsync(
            priceId,
            "payment",
            url,
            url,
            owner.Email,
            owner.Id.ToString(),
            "hidden_plate",
            new Dictionary<string, string>
            {
                ["paymentKind"] = CarPaidExtras.HiddenPlatePaymentKind,
                ["carId"] = car.Id.ToString(),
                ["stayOnPage"] = "true",
            },
            $"hidden-plate:{car.Id}:{DateTime.UtcNow:yyyyMMdd}",
            null,
            cancellationToken);
    }
}

internal sealed class CancelExtraListingCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IStripeService stripe)
    : ICommandHandler<CancelExtraListingCommand>
{
    public async Task<Result> Handle(CancelExtraListingCommand command, CancellationToken cancellationToken)
    {
        Result<(User Owner, Car Car)> loaded = await CarPaidExtras.LoadOwnCarAsync(context, userContext, command.CarId, cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Error);
        }

        (User owner, Car car) = loaded.Value;

        if (car.PaymentStatus is not (CarListingPaymentStatus.Paid or CarListingPaymentStatus.PastDue))
        {
            return Result.Success();
        }

        if (!string.IsNullOrWhiteSpace(car.StripeSubscriptionId))
        {
            await stripe.CancelSubscriptionAsync(car.StripeSubscriptionId, cancellationToken);
        }

        car.PaymentStatus = CarListingPaymentStatus.Cancelled;
        await ExtraListingSlot.WithdrawIfOverQuotaAsync(context, car, owner.Id, cancellationToken);
        car.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Ce se întâmplă cu un anunț când își pierde locul plătit.</summary>
internal static class ExtraListingSlot
{
    /// <summary>
    /// Un anunț publicat pe un loc plătit rămâne publicat doar dacă mai încape în cele incluse în
    /// abonament. Altfel trece pe pauză — nu se șterge, firma îl republică când are loc.
    /// </summary>
    public static async Task WithdrawIfOverQuotaAsync(IApplicationDbContext context, Car car, Guid ownerId, CancellationToken cancellationToken)
    {
        if (car.ListingStatus != ListingStatus.Published)
        {
            return;
        }

        int used = await ListingQuota.CountUsedAsync(context, ownerId, car.Id, cancellationToken);
        if (used >= ListingAllowance.IncludedInFleetPlan)
        {
            car.ListingStatus = ListingStatus.Paused;
        }
    }
}

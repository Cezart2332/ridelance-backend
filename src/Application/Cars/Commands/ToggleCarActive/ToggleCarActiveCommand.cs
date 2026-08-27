using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars.Scoring;
using Domain.Cars;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Cars.Commands.ToggleCarActive;

/// <summary>Comută anunțul între publicat și pe pauză. Întoarce starea rezultată.</summary>
public sealed record ToggleCarActiveCommand(Guid CarId) : ICommand<CarListingStateDto>;

/// <param name="ListingStatus">Intenția proprietarului, după comutare.</param>
/// <param name="Active">
/// Dacă anunțul chiar se vede. Poate fi <c>false</c> cu <c>Published</c>: aprobarea sau plata
/// lipsesc. Interfața are nevoie de amândouă — comutatorul arată intenția, eticheta arată realitatea.
/// </param>
public sealed record CarListingStateDto(string ListingStatus, bool Active);

internal sealed class ToggleCarActiveCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ListingScoreService scoreService)
    : ICommandHandler<ToggleCarActiveCommand, CarListingStateDto>
{
    public async Task<Result<CarListingStateDto>> Handle(ToggleCarActiveCommand command, CancellationToken cancellationToken)
    {
        Result<User> userResult = await CarAccessHelper.GetCurrentUserAsync(context, userContext, cancellationToken);
        if (userResult.IsFailure)
        {
            return Result.Failure<CarListingStateDto>(userResult.Error);
        }

        Car? car = await context.Cars
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<CarListingStateDto>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        Result access = CarAccessHelper.ValidateCarManagement(userResult.Value, car);
        if (access.IsFailure)
        {
            return Result.Failure<CarListingStateDto>(access.Error);
        }

        if (userResult.Value.Role == UserRole.CarPoster && car.ApprovalStatus != CarApprovalStatus.Approved)
        {
            return Result.Failure<CarListingStateDto>(Error.Problem(
                "Car.NotApproved",
                "Anunțul trebuie aprobat de administrator înainte de a fi activat."));
        }

        if (userResult.Value.Role == UserRole.CarPoster && car.PaymentStatus != CarListingPaymentStatus.Paid)
        {
            return Result.Failure<CarListingStateDto>(Error.Problem(
                "Car.PaymentRequired",
                "Anunțul trebuie să aibă plata activă înainte de a fi vizibil."));
        }

        // Comută intenția proprietarului, nu vizibilitatea. Ce se vede în marketplace rămâne
        // derivat din intenție + aprobare + plată; aici se decide doar dacă anunțul e oferit.
        //
        // Din `Draft` se trece în `Published` — un anunț nepublicat încă, pus pe pauză, ar fi
        // însemnat retragerea a ceva ce n-a fost niciodată pe piață. `Archived` nu se întoarce de
        // aici: scoaterea din flotă e o decizie separată, nu opusul unui buton de vizibilitate.
        if (car.ListingStatus == ListingStatus.Archived)
        {
            return Result.Failure<CarListingStateDto>(Error.Problem(
                "Car.Archived",
                "Anunțul e arhivat. Scoate-l din arhivă înainte să-l publici."));
        }

        car.ListingStatus = car.ListingStatus == ListingStatus.Published
            ? ListingStatus.Paused
            : ListingStatus.Published;
        car.UpdatedAtUtc = DateTime.UtcNow;
        await scoreService.RecalculateAsync(car, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return new CarListingStateDto(car.ListingStatus.ToString(), car.Active);
    }
}

using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Commands.UpdateRental;

/// <summary>
/// Corectează o închiriere deschisă.
/// </summary>
/// <remarks>
/// Lipsea: se putea crea și închide o închiriere, dar nu îndrepta o cifră greșită. Singura ieșire
/// era ștergerea, care nu există nici ea — deci datele greșite rămâneau pe contract.
///
/// Ce se schimbă aici **nu urcă niciodată** în valorile implicite ale firmei. Sunt două lucruri
/// diferite: ce s-a convenit într-un contract și ce propune firma data viitoare.
/// </remarks>
public sealed record UpdateRentalCommand(
    Guid RentalId,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    long WeeklyRentBani,
    long DepositBani,
    long OtherCostsBani,
    bool HasKmLimit,
    int? MileageLimit,
    long ExtraKmCostBani,
    string? FuelRule,
    string? FuelLevelAtPickup,
    int? StartMileage,
    IReadOnlyList<string>? Accessories,
    string? AccessoriesOther,
    string? Notes,
    // Câmpurile contractului adăugate după primul formular. `null` = netrimis, rămâne cum era;
    // la text, șirul gol șterge valoarea.
    string? PaymentDueDay = null,
    string? PaymentMethod = null,
    decimal? LatePenaltyPercentPerDay = null,
    long? FranchiseBani = null,
    int? NoticeDays = null,
    string? UnavailabilityRule = null,
    string? AdditionalDriverName = null,
    string? AdditionalDriverCnp = null,
    string? AdditionalDriverLicense = null,
    int? KeysCount = null,
    IReadOnlyList<string>? HandoverDocuments = null,
    string? HandoverDocumentsOther = null,
    string? DamagesAtPickup = null) : ICommand;

internal sealed class UpdateRentalCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpdateRentalCommand>
{
    public async Task<Result> Handle(UpdateRentalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.EndAtUtc <= command.StartAtUtc)
        {
            return Result.Failure(
                Error.Problem("Rental.InvalidPeriod", "Data de predare trebuie să fie după cea de preluare."));
        }

        Result<RentalTerms> parsedTerms = RentalTerms.Parse(new RentalTermsInput(
            command.PaymentDueDay,
            command.PaymentMethod,
            command.LatePenaltyPercentPerDay,
            command.FranchiseBani,
            command.NoticeDays,
            command.UnavailabilityRule));

        if (parsedTerms.IsFailure)
        {
            return Result.Failure(parsedTerms.Error);
        }

        if (command.KeysCount is < 0)
        {
            return Result.Failure(Error.Problem("Rental.Invalid.keysCount", "Numărul de chei nu poate fi negativ."));
        }

        Rental? rental = await context.Rentals
            .FirstOrDefaultAsync(r => r.Id == command.RentalId && r.OwnerUserId == userContext.UserId, cancellationToken);

        if (rental is null)
        {
            return Result.Failure(Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        if (rental.ClosedAtUtc.HasValue)
        {
            return Result.Failure(Error.Problem(
                "Rental.Closed",
                "Închirierea e încheiată. O închiriere încheiată nu se mai modifică."));
        }

        // Aceeași verificare de suprapunere ca la creare, fără rândul curent.
        bool overlaps = await context.Rentals
            .AsNoTracking()
            .AnyAsync(
                r => r.Id != rental.Id
                    && r.CarId == rental.CarId
                    && r.ClosedAtUtc == null
                    && r.Lifecycle != RentalLifecycle.Cancelled
                    && r.StartAtUtc < command.EndAtUtc
                    && command.StartAtUtc < r.EndAtUtc,
                cancellationToken);

        if (overlaps)
        {
            return Result.Failure(Error.Problem(
                "Rental.Overlap",
                "Mașina are deja o închiriere în perioada aleasă."));
        }

        rental.StartAtUtc = command.StartAtUtc;
        rental.EndAtUtc = command.EndAtUtc;
        rental.WeeklyRentBani = command.WeeklyRentBani;
        rental.DepositBani = command.DepositBani;
        rental.OtherCostsBani = command.OtherCostsBani;
        rental.HasKmLimit = command.HasKmLimit;
        rental.MileageLimit = command.HasKmLimit ? command.MileageLimit : null;
        rental.ExtraKmCostBani = command.ExtraKmCostBani;
        rental.FuelRule = command.FuelRule?.Trim();
        rental.FuelLevelAtPickup = command.FuelLevelAtPickup?.Trim();
        rental.StartMileage = command.StartMileage;
        rental.Accessories = command.Accessories?.ToList() ?? [];
        rental.AccessoriesOther = command.AccessoriesOther?.Trim();
        rental.Notes = command.Notes?.Trim();

        RentalTerms terms = parsedTerms.Value;
        rental.PaymentDueDay = terms.PaymentDueDay ?? rental.PaymentDueDay;
        rental.PaymentMethod = terms.PaymentMethod ?? rental.PaymentMethod;
        rental.LatePenaltyPercentPerDay = terms.LatePenaltyPercentPerDay ?? rental.LatePenaltyPercentPerDay;
        rental.FranchiseBani = terms.FranchiseBani ?? rental.FranchiseBani;
        rental.NoticeDays = terms.NoticeDays ?? rental.NoticeDays;
        rental.UnavailabilityRule = terms.UnavailabilityRule ?? rental.UnavailabilityRule;
        rental.AdditionalDriverName = RentalInput.Patch(rental.AdditionalDriverName, command.AdditionalDriverName);
        rental.AdditionalDriverCnp = RentalInput.Patch(rental.AdditionalDriverCnp, command.AdditionalDriverCnp);
        rental.AdditionalDriverLicense = RentalInput.Patch(rental.AdditionalDriverLicense, command.AdditionalDriverLicense);
        rental.KeysCount = command.KeysCount ?? rental.KeysCount;
        if (command.HandoverDocuments is not null)
        {
            rental.HandoverDocuments = RentalInput.CleanList(command.HandoverDocuments);
        }

        rental.HandoverDocumentsOther = RentalInput.Patch(rental.HandoverDocumentsOther, command.HandoverDocumentsOther);
        rental.DamagesAtPickup = RentalInput.Patch(rental.DamagesAtPickup, command.DamagesAtPickup);
        rental.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

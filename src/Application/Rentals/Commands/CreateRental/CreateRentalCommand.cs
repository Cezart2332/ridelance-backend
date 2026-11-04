using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Cars;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Application.Rentals.Checks;

namespace Application.Rentals.Commands.CreateRental;

/// <param name="TenantId">
/// Chiriaș existent. Când e <c>null</c>, se creează unul din <paramref name="Tenant"/> — al doilea
/// contract cu același om nu mai cere retastarea datelor.
/// </param>
public sealed record CreateRentalCommand(
    Guid CarId,
    Guid? TenantId,
    NewTenantInput? Tenant,
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
    // Termenii contractului. Lipsă = se iau din setările flotei (`rentals/defaults`).
    string? PaymentDueDay = null,
    string? PaymentMethod = null,
    decimal? LatePenaltyPercentPerDay = null,
    long? FranchiseBani = null,
    int? NoticeDays = null,
    string? UnavailabilityRule = null,
    // Conducătorul auto suplimentar (clauza 4.3). Fără nume, conduce doar chiriașul.
    string? AdditionalDriverName = null,
    string? AdditionalDriverCnp = null,
    string? AdditionalDriverLicense = null,
    // Predarea (Anexa nr. 1).
    int? KeysCount = null,
    IReadOnlyList<string>? HandoverDocuments = null,
    string? HandoverDocumentsOther = null,
    string? DamagesAtPickup = null) : ICommand<Guid>;

/// <summary>Datele unui chiriaș nou, așa cum vin din formular.</summary>
public sealed record NewTenantInput(
    string Name,
    string Type,
    string? Cnp,
    string? IdSeries,
    string? IdNumber,
    string? Cui,
    string? RegCom,
    string? Address,
    string? Phone,
    string? Email,
    string? DriverLicenseNumber,
    /// <summary>Doar PFA/SRL: cine semnează pentru firmă (clauza 1.2).</summary>
    string? RepresentativeName = null);

internal sealed class CreateRentalCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IRentalCodeGenerator codeGenerator)
    : ICommandHandler<CreateRentalCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRentalCommand command, CancellationToken cancellationToken)
    {
        if (command.TenantId is null && string.IsNullOrWhiteSpace(command.Tenant?.Name))
        {
            return Result.Failure<Guid>(
                Error.Problem("Rental.TenantRequired", "Alege un chiriaș sau completează datele unuia nou."));
        }

        if (command.EndAtUtc <= command.StartAtUtc)
        {
            return Result.Failure<Guid>(
                Error.Problem("Rental.InvalidPeriod", "Data de predare trebuie să fie după cea de preluare."));
        }

        Car? car = await context.Cars
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<Guid>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (car.PostedByUserId != userContext.UserId)
        {
            return Result.Failure<Guid>(Error.Problem("Rental.Forbidden", "Mașina nu îți aparține."));
        }

        // O mașină nu poate fi la doi chiriași în același timp. Verificarea e pe suprapunere de
        // intervale, nu pe „există vreo închiriere activă": o rezervare viitoare care se
        // suprapune e la fel de imposibilă ca una curentă.
        bool overlaps = await context.Rentals
            .AsNoTracking()
            .AnyAsync(
                r => r.CarId == command.CarId
                    && r.ClosedAtUtc == null
                    && r.StartAtUtc < command.EndAtUtc
                    && command.StartAtUtc < r.EndAtUtc,
                cancellationToken);

        if (overlaps)
        {
            return Result.Failure<Guid>(Error.Problem(
                "Rental.Overlap",
                "Mașina are deja o închiriere în perioada aleasă."));
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
            return Result.Failure<Guid>(parsedTerms.Error);
        }

        if (command.KeysCount is < 0)
        {
            return Result.Failure<Guid>(Error.Problem("Rental.Invalid.keysCount", "Numărul de chei nu poate fi negativ."));
        }

        // Ce nu vine din formular se ia din setările flotei, o singură dată, acum. Copiat, nu
        // referit: contractul semnat nu se schimbă când flota își schimbă setările.
        FleetRentalDefaults? defaults = await context.FleetRentalDefaults
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.OwnerUserId == userContext.UserId, cancellationToken);

        RentalTerms terms = parsedTerms.Value;

        Result<Tenant> tenantResult = await ResolveTenantAsync(command, cancellationToken);
        if (tenantResult.IsFailure)
        {
            return Result.Failure<Guid>(tenantResult.Error);
        }

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            PublicCode = await codeGenerator.NextAsync(cancellationToken),
            CarId = car.Id,
            OwnerUserId = userContext.UserId,
            TenantId = tenantResult.Value.Id,
            Lifecycle = RentalLifecycle.Confirmed,
            StartAtUtc = command.StartAtUtc,
            EndAtUtc = command.EndAtUtc,
            WeeklyRentBani = command.WeeklyRentBani,
            DepositBani = command.DepositBani,
            OtherCostsBani = command.OtherCostsBani,
            HasKmLimit = command.HasKmLimit,
            MileageLimit = command.HasKmLimit ? command.MileageLimit : null,
            ExtraKmCostBani = command.ExtraKmCostBani,
            FuelRule = command.FuelRule?.Trim(),
            FuelLevelAtPickup = command.FuelLevelAtPickup?.Trim(),
            StartMileage = command.StartMileage,
            Accessories = command.Accessories?.ToList() ?? [],
            AccessoriesOther = command.AccessoriesOther?.Trim(),
            Notes = command.Notes?.Trim(),
            PaymentDueDay = terms.PaymentDueDay ?? defaults?.PaymentDueDay,
            PaymentMethod = terms.PaymentMethod ?? defaults?.PaymentMethod,
            LatePenaltyPercentPerDay = terms.LatePenaltyPercentPerDay ?? defaults?.LatePenaltyPercentPerDay,
            FranchiseBani = terms.FranchiseBani ?? defaults?.FranchiseBani,
            NoticeDays = terms.NoticeDays ?? defaults?.NoticeDays,
            UnavailabilityRule = terms.UnavailabilityRule ?? defaults?.UnavailabilityRule,
            AdditionalDriverName = RentalInput.Clean(command.AdditionalDriverName),
            AdditionalDriverCnp = RentalInput.Clean(command.AdditionalDriverCnp),
            AdditionalDriverLicense = RentalInput.Clean(command.AdditionalDriverLicense),
            KeysCount = command.KeysCount,
            HandoverDocuments = RentalInput.CleanList(command.HandoverDocuments),
            HandoverDocumentsOther = RentalInput.Clean(command.HandoverDocumentsOther),
            DamagesAtPickup = RentalInput.Clean(command.DamagesAtPickup),
        };

        context.Rentals.Add(rental);

        VehicleTimeline.Record(
            context,
            car.Id,
            VehicleEventType.RentalOpened,
            $"Închiriere {rental.PublicCode} către {tenantResult.Value.Name}",
            rental.Id);

        await context.SaveChangesAsync(cancellationToken);

        return rental.Id;
    }

    /// <summary>
    /// Chiriașul ales, sau unul nou din datele formularului. Nu se caută după nume: doi oameni pot
    /// avea același nume, iar unirea lor tăcută ar pune contractul altcuiva pe masă.
    /// </summary>
    private async Task<Result<Tenant>> ResolveTenantAsync(
        CreateRentalCommand command,
        CancellationToken cancellationToken)
    {
        if (command.TenantId is Guid tenantId)
        {
            Tenant? existing = await context.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId && t.OwnerUserId == userContext.UserId, cancellationToken);

            return existing is null
                ? Result.Failure<Tenant>(Error.NotFound("Tenant.NotFound", "Chiriașul nu a fost găsit."))
                : Result.Success(existing);
        }

        NewTenantInput input = command.Tenant!;
        if (!Enum.TryParse(input.Type, out TenantType tenantType))
        {
            tenantType = TenantType.Individual;
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userContext.UserId,
            Type = tenantType,
            Name = input.Name.Trim(),
            Cnp = input.Cnp?.Trim(),
            IdSeries = input.IdSeries?.Trim(),
            IdNumber = input.IdNumber?.Trim(),
            Cui = input.Cui?.Trim(),
            RegCom = input.RegCom?.Trim(),
            Address = input.Address?.Trim(),
            Phone = input.Phone?.Trim(),
            Email = input.Email?.Trim(),
            DriverLicenseNumber = input.DriverLicenseNumber?.Trim(),
            RepresentativeName = tenantType == TenantType.Individual ? null : RentalInput.Clean(input.RepresentativeName),
        };

        context.Tenants.Add(tenant);
        return Result.Success(tenant);
    }
}

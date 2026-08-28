using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Commands.FillDocumentFields;

/// <summary>
/// Completează, dintr-un singur formular, câmpurile fără de care un document nu se poate genera.
/// </summary>
/// <remarks>
/// <para>
/// Verificatorul (<c>RentalDocumentRequirements</c>) spune ce lipsește și unde se ține fiecare
/// câmp — pe firmă, pe mașină, pe chiriaș sau pe închiriere. Spec §5 cere ca omul să le poată
/// completa pe loc, nu să fie trimis prin trei ecrane să le caute.
/// </para>
/// <para>
/// Scrie **doar** ce primește: un câmp lipsă din comandă rămâne neatins. Așa, formularul poate
/// trimite exact lista care lipsea, fără să știe și fără să rescrie restul datelor firmei sau ale
/// mașinii — care nu sunt ale lui.
/// </para>
/// <para>
/// Toate cele patru obiecte se verifică pe proprietar. Ruta poartă id-ul închirierii, dar
/// completarea atinge și firma, și mașina, și chiriașul: fiecare se caută pe contul apelantului,
/// nu prin id-uri venite din cerere.
/// </para>
/// </remarks>
public sealed record FillDocumentFieldsCommand(
    Guid RentalId,
    string? CompanyLegalName = null,
    string? CompanyCui = null,
    string? CompanyRegisteredOffice = null,
    string? CompanyLegalRepresentative = null,
    string? CarPlateNumber = null,
    string? CarVin = null,
    string? TenantName = null,
    string? TenantAddress = null,
    string? TenantCnp = null,
    string? TenantIdSeries = null,
    string? TenantIdNumber = null,
    string? TenantCui = null,
    int? RentalStartMileage = null) : ICommand;

internal sealed class FillDocumentFieldsCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<FillDocumentFieldsCommand>
{
    public async Task<Result> Handle(FillDocumentFieldsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Rental? rental = await context.Rentals
            .Include(r => r.Tenant)
            .FirstOrDefaultAsync(
                r => r.Id == command.RentalId && r.OwnerUserId == userContext.UserId,
                cancellationToken);

        if (rental is null)
        {
            return Result.Failure(Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        Tenant tenant = rental.Tenant;
        Set(command.TenantName, v => tenant.Name = v);
        Set(command.TenantAddress, v => tenant.Address = v);
        Set(command.TenantCnp, v => tenant.Cnp = v);
        Set(command.TenantIdSeries, v => tenant.IdSeries = v);
        Set(command.TenantIdNumber, v => tenant.IdNumber = v);
        Set(command.TenantCui, v => tenant.Cui = v);

        if (command.RentalStartMileage.HasValue)
        {
            rental.StartMileage = command.RentalStartMileage;
        }

        if (command.CarPlateNumber is not null || command.CarVin is not null)
        {
            Car? car = await context.Cars.FirstOrDefaultAsync(
                c => c.Id == rental.CarId && c.PostedByUserId == userContext.UserId,
                cancellationToken);

            if (car is null)
            {
                return Result.Failure(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
            }

            // Numărul de înmatriculare se scrie cu majuscule oriunde altundeva în aplicație.
            Set(command.CarPlateNumber, v => car.PlateNumber = v.ToUpperInvariant());
            Set(command.CarVin, v => car.Vin = v.ToUpperInvariant());
        }

        bool touchesCompany = command.CompanyLegalName is not null
            || command.CompanyCui is not null
            || command.CompanyRegisteredOffice is not null
            || command.CompanyLegalRepresentative is not null;

        if (touchesCompany)
        {
            CompanyProfile? company = await context.CompanyProfiles.FirstOrDefaultAsync(
                p => p.UserId == userContext.UserId,
                cancellationToken);

            if (company is null)
            {
                // Profilul nu se creează de aici: are slug, tip de proprietar și vizibilități,
                // adică decizii care se iau în ecranul lui, nu într-un dialog de completare.
                return Result.Failure(Error.Problem(
                    "CompanyProfile.Missing",
                    "Completează întâi datele firmei, în Profil firmă."));
            }

            Set(command.CompanyLegalName, v => company.LegalName = v);
            Set(command.CompanyCui, v => company.Cui = v);
            Set(command.CompanyRegisteredOffice, v => company.RegisteredOffice = v);
            Set(command.CompanyLegalRepresentative, v => company.LegalRepresentative = v);
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Scrie doar ce a fost trimis și chiar are conținut.
    /// </summary>
    /// <remarks>
    /// Un câmp absent din cerere e „nu-l atinge"; unul trimis gol ar fi fost „șterge-l", ceea ce
    /// n-are sens într-un formular al cărui rost e să completeze ce lipsește.
    /// </remarks>
    private static void Set(string? value, Action<string> apply)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            apply(value.Trim());
        }
    }
}

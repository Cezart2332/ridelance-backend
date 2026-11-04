using Application.Abstractions.Dossiers;
using Application.Rentals.Documents;
using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;
using Infrastructure.Dossiers.Latex;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Un contract complet, cu toate câmpurile completate: nu rămâne nicio linie goală.
/// </summary>
/// <remarks>
/// Servește și ca model de verificat cu ochii. Cu variabila <c>RENTAL_SAMPLE_DIR</c> setată,
/// testul scrie acolo sursele (<c>contract.tex</c>, <c>predare.tex</c>, <c>restituire.tex</c>),
/// care se compilează cu <c>xelatex</c>, exact ca în producție:
/// <code>RENTAL_SAMPLE_DIR=/tmp/model dotnet test --filter RentalContractSampleTests</code>
/// </remarks>
public sealed class RentalContractSampleTests
{
    [Fact]
    public void A_fully_filled_rental_leaves_no_blank_in_the_contract_or_the_protocols()
    {
        (Rental rental, Car car, CompanyProfile company, Tenant tenant, CheckRecord checkIn, CheckRecord checkOut) = Sample();

        RentalDocumentRequirements.For(RentalDocumentType.RentalContract, rental, car, company, tenant, checkIn).ShouldBeEmpty();

        var documents = new Dictionary<string, RentalDocumentData>
        {
            ["contract"] = RentalDocumentComposer.Compose(RentalDocumentType.RentalContract, rental, car, company, tenant, Conditions, checkIn, checkOut),
            ["predare"] = RentalDocumentComposer.Compose(RentalDocumentType.HandoverProtocol, rental, car, company, tenant, null, checkIn, checkOut),
            ["restituire"] = RentalDocumentComposer.Compose(RentalDocumentType.ReturnProtocol, rental, car, company, tenant, null, checkIn, checkOut),
        };

        string? directory = Environment.GetEnvironmentVariable("RENTAL_SAMPLE_DIR");
        foreach ((string name, RentalDocumentData data) in documents)
        {
            string tex = RentalDocumentLatex.Build(data);
            tex.ShouldNotContain(@"\gol{", customMessage: $"{name}: a rămas o linie de completat");

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, $"{name}.tex"), tex);
            }
        }
    }

    private const string Conditions = "Fumatul în autovehicul este interzis.\n\nAutovehiculul se spală pe cheltuiala Locatarului înainte de restituire.";

    private static (Rental, Car, CompanyProfile, Tenant, CheckRecord, CheckRecord) Sample()
    {
        var tenant = new Tenant
        {
            Type = TenantType.Individual,
            Name = "Adrian Popescu",
            Cnp = "1900101123456",
            IdSeries = "CJ",
            IdNumber = "123456",
            Address = "Cluj-Napoca, Str. Memorandumului 10",
            Phone = "0722 000 000",
            DriverLicenseNumber = "CJ987654",
        };

        var rental = new Rental
        {
            PublicCode = "RL-000042",
            Tenant = tenant,
            CreatedAtUtc = new DateTime(2026, 8, 30, 10, 0, 0, DateTimeKind.Utc),
            StartAtUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            EndAtUtc = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc),
            WeeklyRentBani = 45_000,
            DepositBani = 100_000,
            OtherCostsBani = 20_000,
            HasKmLimit = true,
            MileageLimit = 2_000,
            ExtraKmCostBani = 50,
            FuelRule = "plin la predare, plin la restituire",
            FuelLevelAtPickup = "Plin",
            StartMileage = 45_000,
            Accessories = ["Chei", "Stingător", "Triunghi", "Trusă medicală"],
            AccessoriesOther = "Suport telefon",
            Notes = "Autovehiculul a fost predat spălat.",
            PaymentDueDay = DayOfWeek.Monday,
            PaymentMethod = RentalPaymentMethod.BankTransfer,
            LatePenaltyPercentPerDay = 0.5m,
            FranchiseBani = 150_000,
            NoticeDays = 15,
            UnavailabilityRule = RentalUnavailabilityRule.Proportional,
            AdditionalDriverName = "Mihai Pop",
            AdditionalDriverCnp = "1850202123456",
            AdditionalDriverLicense = "CJ123456",
            KeysCount = 2,
            HandoverDocuments = [HandoverDocumentCodes.RegistrationCertificate, HandoverDocumentCodes.Rca],
            HandoverDocumentsOther = "ITP valabil",
            DamagesAtPickup = "Zgârietură ușoară pe bara spate, partea dreaptă",
        };

        var checkIn = new CheckRecord
        {
            Kind = CheckKind.CheckIn,
            OccurredAtUtc = rental.StartAtUtc,
            Mileage = 45_000,
            FuelLevel = "Plin",
            KeysCount = 2,
            Documents = [HandoverDocumentCodes.RegistrationCertificate, HandoverDocumentCodes.Rca],
            DocumentsOther = "ITP valabil",
            Accessories = ["Chei", "Stingător", "Triunghi", "Trusă medicală", "Suport telefon"],
            Damages = "Zgârietură ușoară pe bara spate, partea dreaptă",
            Notes = "Autovehiculul a fost predat spălat.",
            Photos = [new CheckPhoto { Slot = CheckPhotoSlot.Front }, new CheckPhoto { Slot = CheckPhotoSlot.Rear }],
        };

        var checkOut = new CheckRecord
        {
            Kind = CheckKind.CheckOut,
            OccurredAtUtc = rental.EndAtUtc,
            Mileage = 52_300,
            FuelLevel = "3/4",
            KeysCount = 2,
            Documents = [HandoverDocumentCodes.RegistrationCertificate, HandoverDocumentCodes.Rca],
            DocumentsOther = "ITP valabil",
            Accessories = ["Chei", "Stingător", "Triunghi", "Trusă medicală", "Suport telefon"],
            Damages = "Fără avarii noi față de predare",
            Notes = "Se reține contravaloarea a 1/4 rezervor.",
            Photos = [new CheckPhoto { Slot = CheckPhotoSlot.Front }],
        };

        var car = new Car { Brand = "Dacia", Model = "Logan", Year = 2022, PlateNumber = "B 123 RID", Vin = "UU1TEST0000000001", Color = "Alb" };

        var company = new CompanyProfile
        {
            OwnerType = OwnerType.Srl,
            LegalName = "Flota Test SRL",
            Cui = "RO12345678",
            RegCom = "J40/123/2024",
            RegisteredOffice = "București, Str. Exemplu 1, Sector 1",
            Iban = "RO49AAAA1B31007593840000",
            LegalRepresentative = "Ion Ionescu",
            LegalRepresentativeRole = "administrator",
            Phone = "0700 000 000",
        };

        return (rental, car, company, tenant, checkIn, checkOut);
    }
}

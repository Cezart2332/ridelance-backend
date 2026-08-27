using Application.Rentals.Documents;
using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Modalul din spec §5 trebuie să ceară **exact** câmpurile lipsă.
/// </summary>
/// <remarks>
/// Greșeala pe care o prinde: un verificator prea lacom, care cere date pe care documentul nu le
/// folosește. Fiecare câmp în plus e o întrebare pusă degeaba unui om care voia doar un contract.
/// </remarks>
public sealed class RentalDocumentRequirementsTests
{
    private static CompanyProfile Company() => new()
    {
        LegalName = "Flota Test SRL",
        Cui = "RO12345678",
        RegisteredOffice = "București, Sector 1",
        LegalRepresentative = "Ion Popescu",
    };

    private static Car CompleteCar() => new()
    {
        Brand = "Dacia",
        Model = "Logan",
        Year = 2022,
        PlateNumber = "B 123 RID",
        Vin = "UU1TEST0000000001",
    };

    private static Tenant Individual() => new()
    {
        Type = TenantType.Individual,
        Name = "Adrian Popescu",
        Cnp = "1900101123456",
        IdSeries = "RD",
        IdNumber = "123456",
        Address = "București, Str. Test 1",
    };

    private static Rental Rental(int? startMileage = 45_000) => new()
    {
        PublicCode = "RL-000001",
        StartMileage = startMileage,
    };

    [Fact]
    public void A_complete_set_of_data_asks_for_nothing()
    {
        RentalDocumentRequirements
            .For(RentalDocumentType.RentalContract, Rental(), CompleteCar(), Company(), Individual())
            .ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_plate_and_vin_are_reported_by_field_not_by_form()
    {
        Car car = CompleteCar();
        car.PlateNumber = null;
        car.Vin = null;

        IReadOnlyList<MissingField> missing = RentalDocumentRequirements
            .For(RentalDocumentType.RentalContract, Rental(), car, Company(), Individual());

        missing.Select(m => m.Field).ShouldBe(["car.plateNumber", "car.vin"]);
        missing.ShouldAllBe(m => m.Owner == "car");
    }

    [Fact]
    public void A_company_tenant_is_asked_for_its_cui_not_for_a_cnp()
    {
        var tenant = new Tenant
        {
            Type = TenantType.Srl,
            Name = "Chiriaș SRL",
            Address = "Cluj, Str. Test 2",
        };

        IReadOnlyList<MissingField> missing = RentalDocumentRequirements
            .For(RentalDocumentType.RentalContract, Rental(), CompleteCar(), Company(), tenant);

        missing.Select(m => m.Field).ShouldBe(["tenant.cui"]);
    }

    [Fact]
    public void An_individual_is_asked_for_the_identity_document_a_company_never_is()
    {
        Tenant tenant = Individual();
        tenant.IdSeries = null;
        tenant.IdNumber = null;

        IReadOnlyList<MissingField> missing = RentalDocumentRequirements
            .For(RentalDocumentType.RentalContract, Rental(), CompleteCar(), Company(), tenant);

        missing.Select(m => m.Field).ShouldBe(["tenant.idSeries", "tenant.idNumber"]);
    }

    [Fact]
    public void The_contract_does_not_need_a_mileage_but_the_handover_protocol_does()
    {
        // Kilometrajul nu ține de contract: contractul spune ce s-a convenit, procesul-verbal
        // spune ce s-a predat.
        RentalDocumentRequirements
            .For(RentalDocumentType.RentalContract, Rental(startMileage: null), CompleteCar(), Company(), Individual())
            .ShouldBeEmpty();

        RentalDocumentRequirements
            .For(RentalDocumentType.HandoverProtocol, Rental(startMileage: null), CompleteCar(), Company(), Individual())
            .Select(m => m.Field)
            .ShouldBe(["rental.startMileage"]);
    }

    [Fact]
    public void Without_a_company_profile_there_is_no_contracting_party()
    {
        IReadOnlyList<MissingField> missing = RentalDocumentRequirements
            .For(RentalDocumentType.RentalContract, Rental(), CompleteCar(), company: null, Individual());

        missing.Select(m => m.Field).ShouldBe(["company.profile"]);
    }
}

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
        RegCom = "J40/123/2024",
        Iban = "RO49AAAA1B31007593840000",
        RegisteredOffice = "București, Sector 1",
        LegalRepresentative = "Ion Popescu",
        LegalRepresentativeRole = "administrator",
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

    /// <summary>O închiriere cu toți termenii conveniți și predarea notată.</summary>
    private static Rental Rental(int? startMileage = 45_000) => new()
    {
        PublicCode = "RL-000001",
        StartMileage = startMileage,
        FuelLevelAtPickup = "Plin",
        KeysCount = 2,
        PaymentDueDay = DayOfWeek.Monday,
        PaymentMethod = RentalPaymentMethod.BankTransfer,
        FranchiseBani = 150_000,
        NoticeDays = 15,
        UnavailabilityRule = RentalUnavailabilityRule.NotDue,
    };

    private static IEnumerable<string> Fields(RentalDocumentType type, Rental rental, Tenant? tenant = null, CompanyProfile? company = null, CheckRecord? checkIn = null) =>
        RentalDocumentRequirements
            .For(type, rental, CompleteCar(), company ?? Company(), tenant ?? Individual(), checkIn)
            .Select(m => m.Field);

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
            RegCom = "J12/1/2020",
            RepresentativeName = "Maria Ionescu",
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

    [Fact]
    public void The_contract_asks_for_every_agreed_term_it_prints()
    {
        var rental = new Rental { PublicCode = "RL-000001" };

        IReadOnlyList<MissingField> missing = RentalDocumentRequirements
            .For(RentalDocumentType.RentalContract, rental, CompleteCar(), Company(), Individual());

        missing.Select(m => m.Field).ShouldBe([
            "rental.paymentDueDay",
            "rental.paymentMethod",
            "rental.unavailabilityRule",
            "rental.franchiseBani",
            "rental.noticeDays",
        ]);
        missing.ShouldAllBe(m => m.Owner == "rental");
    }

    [Fact]
    public void The_contract_asks_for_the_company_data_printed_in_clause_1_1()
    {
        CompanyProfile company = Company();
        company.RegCom = null;
        company.Iban = " ";
        company.LegalRepresentativeRole = null;

        Fields(RentalDocumentType.RentalContract, Rental(), company: company)
            .ShouldBe(["company.legalRepresentativeRole", "company.regCom", "company.iban"]);

        // Procesele-verbale nu tipăresc nimic din ele, deci nu le cer.
        Fields(RentalDocumentType.HandoverProtocol, Rental(), company: company).ShouldBeEmpty();
    }

    [Fact]
    public void A_km_limit_without_the_numbers_is_reported()
    {
        Rental rental = Rental();
        rental.HasKmLimit = true;

        Fields(RentalDocumentType.RentalContract, rental).ShouldBe(["rental.mileageLimit", "rental.extraKmCostBani"]);

        rental.MileageLimit = 2_000;
        rental.ExtraKmCostBani = 50;
        Fields(RentalDocumentType.RentalContract, rental).ShouldBeEmpty();
    }

    [Fact]
    public void A_penalty_and_an_additional_driver_are_never_required()
    {
        // Șablonul le are „dacă părțile au convenit”: lipsa lor e o alegere, nu un câmp uitat.
        Rental rental = Rental();
        rental.LatePenaltyPercentPerDay = null;
        rental.AdditionalDriverName = null;

        Fields(RentalDocumentType.RentalContract, rental).ShouldBeEmpty();
    }

    [Fact]
    public void An_srl_tenant_needs_a_representative_a_pfa_does_not()
    {
        var srl = new Tenant { Type = TenantType.Srl, Name = "Chiriaș SRL", Address = "Cluj", Cui = "RO1" };
        var pfa = new Tenant { Type = TenantType.Pfa, Name = "Pop Ion PFA", Address = "Cluj", Cui = "RO2" };

        Fields(RentalDocumentType.RentalContract, Rental(), srl).ShouldBe(["tenant.regCom", "tenant.representativeName"]);
        Fields(RentalDocumentType.RentalContract, Rental(), pfa).ShouldBe(["tenant.regCom"]);
    }

    [Fact]
    public void A_car_without_a_year_is_reported_for_the_contract()
    {
        Car car = CompleteCar();
        car.Year = 0;

        RentalDocumentRequirements
            .For(RentalDocumentType.RentalContract, Rental(), car, Company(), Individual())
            .Select(m => m.Field)
            .ShouldBe(["car.year"]);
    }

    [Fact]
    public void The_handover_protocol_asks_for_fuel_and_keys()
    {
        Rental rental = Rental();
        rental.FuelLevelAtPickup = null;
        rental.KeysCount = null;

        Fields(RentalDocumentType.HandoverProtocol, rental).ShouldBe(["rental.fuelLevelAtPickup", "rental.keysCount"]);

        // „Chei” bifat în accesorii, ca înainte de câmpul dedicat, înseamnă cel puțin un set.
        rental.Accessories = ["Chei"];
        Fields(RentalDocumentType.HandoverProtocol, rental).ShouldBe(["rental.fuelLevelAtPickup"]);
    }

    [Fact]
    public void A_recorded_check_in_stands_in_for_the_handover_fields()
    {
        var rental = new Rental { PublicCode = "RL-000001" };
        var checkIn = new CheckRecord { Kind = CheckKind.CheckIn, Mileage = 45_000, FuelLevel = "3/4", KeysCount = 1 };

        Fields(RentalDocumentType.HandoverProtocol, rental, checkIn: checkIn).ShouldBeEmpty();
    }
}

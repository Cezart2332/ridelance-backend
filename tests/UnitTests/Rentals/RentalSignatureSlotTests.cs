using Application.Abstractions.Dossiers;
using Application.Rentals.Documents;
using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;
using Shouldly;
using Xunit;

namespace UnitTests.Rentals;

/// <summary>
/// Ce linie de semnătură e a cui.
/// </summary>
/// <remarks>
/// Numărul liniei e singura legătură dintre semnătura stocată și locul în care se tipărește: dacă
/// ordinea din <c>SignatureLines</c> s-ar schimba fără constante, semnătura firmei ar ajunge tăcut
/// pe linia chiriașului. Un document greșit care arată perfect e cel mai greu de observat.
/// </remarks>
public sealed class RentalSignatureSlotTests
{
    [Fact]
    public void Prima_linie_e_a_firmei_a_doua_a_chiriasului()
    {
        RentalDocumentData data = Compose();

        data.SignatureLines[RentalDocumentComposer.CompanySignatureSlot - 1].ShouldBe("Flota Test SRL");
        data.SignatureLines[RentalDocumentComposer.TenantSignatureSlot - 1].ShouldBe("Adrian Popescu");
    }

    [Fact]
    public void Documentul_are_exact_doua_linii_de_semnatura()
    {
        Compose().SignatureLines.Count.ShouldBe(2);
    }

    private static RentalDocumentData Compose() => RentalDocumentComposer.Compose(
        RentalDocumentType.RentalContract,
        new Rental { PublicCode = "RL-000001", StartMileage = 45_000 },
        new Car { Brand = "Dacia", Model = "Logan", Year = 2022, PlateNumber = "B 123 RID", Vin = "UU1TEST0000000001" },
        new CompanyProfile { LegalName = "Flota Test SRL", Cui = "RO12345678" },
        new Tenant { Type = TenantType.Individual, Name = "Adrian Popescu" },
        null);
}

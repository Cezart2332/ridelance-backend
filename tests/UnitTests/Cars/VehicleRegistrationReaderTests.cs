using Application.Abstractions.Ai;
using Application.Cars.Commands.ScanRegistration;
using Application.Documents.AiVerification;
using Domain.Documents;
using Shouldly;
using Xunit;

namespace UnitTests.Cars;

/// <summary>
/// Citirea talonului pentru precompletarea numărului de înmatriculare.
///
/// Testele nu ating modelul: îi fabrică răspunsul. Ce se verifică e ce ajunge în formular —
/// inclusiv cazul în care s-a citit ceva ce nu arată a plăcuță, care se întoarce totuși, marcat,
/// pentru că un câmp gol fără explicație e mai prost decât o valoare de corectat.
/// </summary>
public sealed class VehicleRegistrationReaderTests
{
    /// <summary>Specificațiile reale din catalog — dacă cheile se schimbă acolo, testele cad aici.</summary>
    private static readonly List<ExtractedFieldSpec> Specs =
        DocumentAiCatalog.For(DocumentCategory.Talon)!.FieldSpecs
            .Where(f => f.Key is "plate_number" or "vin")
            .ToList();

    private static DocumentAiAnalysisResult Response(
        string? plate = null,
        string? vin = null,
        double confidence = 0.9,
        bool matchesType = true,
        bool readable = true,
        string detectedType = "Certificat de înmatriculare")
    {
        var fields = new List<AiFieldResult>();
        if (plate is not null)
        {
            fields.Add(new AiFieldResult("plate_number", plate, confidence));
        }

        if (vin is not null)
        {
            fields.Add(new AiFieldResult("vin", vin, confidence));
        }

        return new DocumentAiAnalysisResult(
            matchesType, readable, IssuedOn: null, ExpiresAt: null, detectedType, Reason: "", fields, confidence);
    }

    [Fact]
    public void Specs_still_exist_in_the_catalog()
    {
        Specs.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("B 123 ABC", "B123ABC")]
    [InlineData("b123abc", "B123ABC")]
    [InlineData("  CJ 07 XYZ  ", "CJ07XYZ")]
    public void Plate_is_normalised_without_spaces_and_uppercased(string raw, string expected)
    {
        VehicleRegistrationScan scan = VehicleRegistrationReader.Read(Response(plate: raw), Specs);

        scan.PlateNumber!.Value.ShouldBe(expected);
        scan.PlateNumber.MatchesFormat.ShouldBeTrue();
    }

    [Fact]
    public void Plate_that_does_not_look_like_a_plate_is_returned_but_flagged()
    {
        VehicleRegistrationScan scan = VehicleRegistrationReader.Read(Response(plate: "ABCDEF"), Specs);

        scan.PlateNumber!.Value.ShouldBe("ABCDEF");
        scan.PlateNumber.MatchesFormat.ShouldBeFalse();
        // Încrederea se plafonează când validatorul nu trece: modelul n-are voie să fie sigur
        // pe ceva ce nu are formatul cerut.
        scan.PlateNumber.Confidence.ShouldBeLessThanOrEqualTo(0.30);
    }

    [Fact]
    public void Valid_vin_passes_the_format_check()
    {
        VehicleRegistrationScan scan = VehicleRegistrationReader.Read(Response(vin: "WF0AXXGCDA8R12345"), Specs);

        scan.Vin!.Value.ShouldBe("WF0AXXGCDA8R12345");
        scan.Vin.MatchesFormat.ShouldBeTrue();
    }

    [Fact]
    public void Short_vin_is_flagged()
    {
        VehicleRegistrationScan scan = VehicleRegistrationReader.Read(Response(vin: "WF0AXX"), Specs);

        scan.Vin!.MatchesFormat.ShouldBeFalse();
    }

    [Fact]
    public void Nothing_read_from_a_valid_talon_explains_itself()
    {
        VehicleRegistrationScan scan = VehicleRegistrationReader.Read(Response(), Specs);

        scan.PlateNumber.ShouldBeNull();
        scan.Vin.ShouldBeNull();
        scan.Note.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Wrong_document_says_what_it_actually_is()
    {
        VehicleRegistrationScan scan = VehicleRegistrationReader.Read(
            Response(matchesType: false, detectedType: "o factură de service"),
            Specs);

        scan.Note!.ShouldContain("factură de service");
    }

    [Fact]
    public void Unreadable_photo_asks_for_a_better_one()
    {
        VehicleRegistrationScan scan = VehicleRegistrationReader.Read(Response(readable: false), Specs);

        scan.Note!.ShouldContain("neclară");
    }

    [Fact]
    public void A_successful_read_carries_no_note()
    {
        VehicleRegistrationScan scan = VehicleRegistrationReader.Read(Response(plate: "B 123 ABC"), Specs);

        scan.Note.ShouldBeNull();
    }
}

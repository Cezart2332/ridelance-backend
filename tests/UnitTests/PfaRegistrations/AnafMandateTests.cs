using Application.Abstractions.Dossiers;
using Application.PfaRegistrations.Onboarding.AnafMandate;
using Domain.PfaRegistrations.CompanyFormation;
using Infrastructure.Dossiers;
using QuestPDF.Infrastructure;
using Shouldly;
using UglyToad.PdfPig;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>Împuternicirea ANAF: ce date intră, ce lipsește și cum iese textul în PDF.</summary>
public sealed class AnafMandateTests
{
    static AnafMandateTests() => QuestPDF.Settings.License = LicenseType.Community;

    private static readonly AnafMandatar Mandatar = new(
        "IONESCU ANDREI-VICTOR", "5010519420017", "București, Strada Constantin Brâncuși nr. 9, Bloc D15, scara A",
        "RK", "956806", "victor.ionescu@ridelance.ro");

    private static Dictionary<string, string> OcrIdentity() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["nume"] = "Popescu",
        ["prenume"] = "Ion-Andrei",
        ["cnp"] = "1900101123456",
        ["serie_act"] = "cj",
        ["numar_act"] = "123456",
        ["autoritate_emitenta"] = "SPCLEP Cluj-Napoca",
        ["data_emiterii"] = "2021-03-15",
        ["domiciliu_judet"] = "Cluj",
        ["domiciliu_localitate"] = "Cluj-Napoca",
        ["domiciliu_strada"] = "Memorandumului",
        ["domiciliu_numar"] = "28",
        ["domiciliu_apartament"] = "7",
    };

    [Fact]
    public void Mandant_FromIdentityFields_HasEverything()
    {
        AnafMandant mandant = AnafMandateContent.Mandant(
            OcrIdentity(), "POPESCU ION-ANDREI PFA", "Cluj-Napoca, str. Memorandumului nr. 28", "45123456", "F12/345/2024");

        mandant.FullName.ShouldBe("POPESCU ION-ANDREI");
        mandant.IdSeries.ShouldBe("CJ");
        mandant.IdIssuedOn.ShouldBe(new DateOnly(2021, 3, 15));
        mandant.Domicile.ShouldBe("Cluj-Napoca, jud. Cluj, str. Memorandumului nr. 28, ap. 7");
        AnafMandateContent.Missing(mandant, Mandatar).ShouldBeEmpty();
    }

    [Fact]
    public void Missing_NamesWhatTheAdminHasToComplete()
    {
        Dictionary<string, string> identity = OcrIdentity();
        identity.Remove("cnp");
        identity.Remove("data_emiterii");

        AnafMandant mandant = AnafMandateContent.Mandant(identity, "POPESCU ION-ANDREI PFA", null, null, null);
        IReadOnlyList<string> missing = AnafMandateContent.Missing(mandant, Mandatar with { Cnp = null });

        missing.ShouldBe(
        [
            "CNP-ul titularului",
            "data eliberării CI",
            "sediul profesional",
            "CUI-ul",
            "datele mandatarului (configurarea serverului, AnafMandatar)",
        ]);
        AnafMandateContent.Note(missing)!.ShouldStartWith("Lipsește: CNP-ul titularului, data eliberării CI");
    }

    [Fact]
    public void FormationData_WinsOverDocument_AndDocumentFillsTheGaps()
    {
        var person = new PersoanaFizica
        {
            Nume = "Popescu",
            Prenume = "Ion",
            SerieAct = "KX",
            NumarAct = "654321",
        };

        Dictionary<string, string> identity = AnafMandateContent.FieldsOf(person, cnp: null);
        AnafMandateContent.FillGaps(identity, OcrIdentity());

        identity["prenume"].ShouldBe("Ion");
        identity["serie_act"].ShouldBe("KX");
        identity["cnp"].ShouldBe("1900101123456");
        identity["domiciliu_strada"].ShouldBe("Memorandumului");
    }

    [Fact]
    public void Address_InBucharest_DoesNotRepeatTheCounty()
    {
        AnafMandateContent.Address("Municipiul București", "București Sector 3", "Unirii", "10", block: "A1")
            .ShouldBe("București Sector 3, str. Unirii nr. 10, bl. A1");
        AnafMandateContent.Address("București", "București", "Unirii", "10")
            .ShouldBe("București, str. Unirii nr. 10");
        AnafMandateContent.Address("Cluj", "Cluj-Napoca", null, "10").ShouldBeNull();
    }

    [Fact]
    public void Pdf_FillsTheTemplate()
    {
        AnafMandant mandant = AnafMandateContent.Mandant(
            OcrIdentity(), "POPESCU ION-ANDREI PFA", "Cluj-Napoca, str. Memorandumului nr. 28", "45123456", "F12/345/2024");

        string text = TextOf(new AnafMandatePdfGenerator().Generate(
            new AnafMandateData("ANAF-000001", new DateOnly(2026, 10, 8), mandant, Mandatar)));

        text.ShouldContain("ÎMPUTERNICIRE");
        text.ShouldContain("Nr. ANAF-000001 / 08.10.2026");
        text.ShouldContain("Subsemnatul/Subsemnata POPESCU ION-ANDREI, CNP 1900101123456");
        text.ShouldContain("CI seria CJ nr. 123456, eliberată de SPCLEP Cluj-Napoca la data de 15.03.2021");
        text.ShouldContain("CUI 45123456, înregistrat la ONRC sub nr. F12/345/2024, denumit(ă) în continuare Mandant");
        text.ShouldContain("IONESCU ANDREI-VICTOR, CNP 5010519420017");
        text.ShouldContain("8. să acceseze, transmită și descarce documente prin serviciile RO e-Factura");
        text.ShouldContain("Titular POPESCU ION-ANDREI PFA");
        text.ShouldContain("Data: 08.10.2026");
        text.ShouldNotContain("{{");
        text.ShouldNotContain("—");
    }

    [Fact]
    public void Pdf_WithoutOnrcNumber_LeavesThePhraseOut()
    {
        AnafMandant mandant = AnafMandateContent.Mandant(OcrIdentity(), "POPESCU ION-ANDREI PFA", "Cluj", "45123456", null);

        string text = TextOf(new AnafMandatePdfGenerator().Generate(
            new AnafMandateData("ANAF-000002", new DateOnly(2026, 10, 8), mandant, Mandatar with { Email = null })));

        text.ShouldContain("CUI 45123456, denumit(ă) în continuare Mandant");
        text.ShouldNotContain("ONRC");
        text.ShouldContain("e-mail —");
    }

    private static string TextOf(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(' ', document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }
}

using System.Text;
using System.Xml.Linq;
using Application.Abstractions.Anaf;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Infrastructure.Accounting.Anaf;
using Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>B4: XML-ul D100 / D301 / D390 pe cazul Ion Popescu, XSD-ul oficial și corecțiile lui.</summary>
public sealed class AnafDeclarationXmlTests
{
    private static readonly XNamespace D100 = D100MapperV2.Namespace;
    private static readonly XNamespace D301 = D301MapperV1.Namespace;
    private static readonly XNamespace D390 = D390MapperV3.Namespace;

    private readonly AnafDeclarationXmlService _service = new();

    [Theory]
    [InlineData(DeclarationType.D100, "v2-20220224", AnafSchemaCorrections.D100V2)]
    [InlineData(DeclarationType.D301, "v1-20200130", AnafSchemaCorrections.D301V1)]
    [InlineData(DeclarationType.D390, "v3-20210212", AnafSchemaCorrections.D390V3)]
    public void Ion_xml_passes_the_official_schema_and_the_content_check(DeclarationType type, string version, string xsd)
    {
        AnafDeclarationInput input = AnafTestSupport.Input(type);

        byte[] xml = _service.Build(version, input);

        _service.ValidateSchema(xsd, xml).ShouldBeEmpty();
        _service.VerifyContent(version, input, xml).ShouldBeEmpty();
        Encoding.UTF8.GetString(xml).ShouldStartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
    }

    [Fact]
    public void D100_has_one_obligation_634_in_whole_lei()
    {
        XElement root = Build(DeclarationType.D100, "v2-20220224");

        root.Name.ShouldBe(D100 + "declaratie100");
        ((string?)root.Attribute("luna"), (string?)root.Attribute("an"), (string?)root.Attribute("cui")).ShouldBe(("8", "2026", "12345674"));
        ((string?)root.Attribute("nume_declar"), (string?)root.Attribute("prenume_declar"), (string?)root.Attribute("functie_declar")).ShouldBe(("Popescu", "Ion", "TITULAR"));
        ((string?)root.Attribute("adresa")).ShouldBe("Str. Exemplu 1, Bucuresti");
        ((string?)root.Attribute("totalPlata_A")).ShouldBe("40");

        XElement obligation = root.Elements(D100 + "obligatie").ShouldHaveSingleItem();
        ((string?)obligation.Attribute("cod_oblig")).ShouldBe("634");
        ((string?)obligation.Attribute("cod_bugetar")).ShouldBe("5503XXXXXX");
        ((string?)obligation.Attribute("scadenta")).ShouldBe("25.09.2026");
        // 10 | 634 | 01 | 0826 | 250926 | 0 | 0 | 00 | suma de control
        ((string?)obligation.Attribute("nr_evid")).ShouldBe("10634010826250926000055");
        ((string?)obligation.Attribute("suma_dat"), (string?)obligation.Attribute("suma_plata")).ShouldBe(("20", "20"));
    }

    [Fact]
    public void D301_lists_each_invoice_in_sections_4_and_4_1()
    {
        XElement root = Build(DeclarationType.D301, "v1-20200130");

        ((string?)root.Attribute("pers_inreg"), (string?)root.Attribute("temei"), (string?)root.Attribute("d_rec")).ShouldBe(("2", "0", "0"));
        ((string?)root.Attribute("banca"), (string?)root.Attribute("cont")).ShouldBe(("Banca Transilvania", "RO49AAAA1B31007593840000"));
        ((string?)root.Attribute("nr_evid")).ShouldBe("10301010826250926000046");
        ((string?)root.Attribute("baza4"), (string?)root.Attribute("tva4")).ShouldBe(("1600", "336"));
        ((string?)root.Attribute("baza5"), (string?)root.Attribute("tva5")).ShouldBe(("1600", "336"));
        ((string?)root.Attribute("baza1"), (string?)root.Attribute("tva1")).ShouldBe(("0", "0"));
        ((string?)root.Attribute("totalPlata_A")).ShouldBe("3872");

        List<XElement> sections = [.. root.Elements(D301 + "sectiune")];
        sections.Select(s => ((string?)s.Attribute("tip_operatie"), (string?)s.Attribute("nr_doc"), (string?)s.Attribute("tva"))).ShouldBe(
        [
            ("4", "EE-BOLT-2026-08-1000", "210"),
            ("5", "EE-BOLT-2026-08-1000", "210"),
            ("4", "UBR-RO-2026-08-1002", "126"),
            ("5", "UBR-RO-2026-08-1002", "126"),
        ]);
        sections[0].Attribute("data_doc")!.Value.ShouldBe("31.08.2026");
        (sections[0].Attribute("tip_valuta")!.Value, sections[0].Attribute("curs_valutar")!.Value).ShouldBe(("RON", "1"));
    }

    [Fact]
    public void D301_keeps_the_invoice_currency_and_exchange_rate()
    {
        AnafDeclarationInput input = AnafTestSupport.Input(DeclarationType.D301) with
        {
            Lines = [new AnafDeclarationLine("EE-1", new DateOnly(2026, 8, 31), 200.13m, "EUR", 4.9775m, 996.15m, 209.19m, "Bolt Operations OÜ", "EE", "EE102090374")],
            Amount = 209.19m,
        };

        byte[] xml = _service.Build("v1-20200130", input);
        XElement section = XDocument.Parse(Encoding.UTF8.GetString(xml)).Root!.Elements(D301 + "sectiune").First();

        (section.Attribute("val_valuta")!.Value, section.Attribute("tip_valuta")!.Value, section.Attribute("curs_valutar")!.Value).ShouldBe(("200.13", "EUR", "4.9775"));
        (section.Attribute("baza")!.Value, section.Attribute("tva")!.Value).ShouldBe(("996.15", "209.19"));
        // INT(2 × (996,15 + 209,19))
        XDocument.Parse(Encoding.UTF8.GetString(xml)).Root!.Attribute("totalPlata_A")!.Value.ShouldBe("2410");
        _service.ValidateSchema(AnafSchemaCorrections.D301V1, xml).ShouldBeEmpty();
        _service.VerifyContent("v1-20200130", input, xml).ShouldBeEmpty();
    }

    [Fact]
    public void D390_has_one_service_operation_per_supplier()
    {
        XElement root = Build(DeclarationType.D390, "v3-20210212");

        ((string?)root.Attribute("totalPlata_A")).ShouldBe("1602");
        XElement summary = root.Element(D390 + "rezumat")!;
        ((string?)summary.Attribute("nrOPI"), (string?)summary.Attribute("bazaS"), (string?)summary.Attribute("total_baza")).ShouldBe(("2", "1600", "1600"));
        root.Elements(D390 + "cos").ShouldBeEmpty();
        root.Elements(D390 + "operatie").Select(o => ((string?)o.Attribute("tip"), (string?)o.Attribute("tara"), (string?)o.Attribute("codO"), (string?)o.Attribute("denO"), (string?)o.Attribute("baza")))
            .ShouldBe([("S", "EE", "102090374", "Bolt Operations OU", "1000"), ("S", "NL", "852071589B01", "Uber B.V.", "600")]);
    }

    [Fact]
    public void Content_check_catches_a_changed_amount()
    {
        AnafDeclarationInput input = AnafTestSupport.Input(DeclarationType.D100);
        string xml = Encoding.UTF8.GetString(_service.Build("v2-20220224", input)).Replace("suma_dat=\"20\"", "suma_dat=\"25\"", StringComparison.Ordinal);

        IReadOnlyList<ValidationMessage> messages = _service.VerifyContent("v2-20220224", input, Encoding.UTF8.GetBytes(xml));

        messages.Select(m => m.Field).ShouldBe(["suma_dat", "suma_plata", "totalPlata_A"], ignoreOrder: true);
    }

    [Fact]
    public void D100_with_bani_asks_for_whole_lei()
    {
        AnafDeclarationInput input = AnafTestSupport.Input(DeclarationType.D100) with { Amount = 20.40m };

        IReadOnlyList<ValidationMessage> messages = _service.VerifyContent("v2-20220224", input, _service.Build("v2-20220224", input));

        messages.ShouldHaveSingleItem().Field.ShouldBe("suma_dat");
        messages[0].Text.ShouldContain("lei întregi");
    }

    [Fact]
    public void Schema_errors_point_to_the_field()
    {
        AnafDeclarationInput input = AnafTestSupport.Input(DeclarationType.D301) with
        {
            Taxpayer = AnafTestSupport.Ion with { Cui = "RO12345674" },
        };

        IReadOnlyList<ValidationMessage> messages = _service.ValidateSchema(AnafSchemaCorrections.D301V1, _service.Build("v1-20200130", input));

        messages.ShouldHaveSingleItem().Field.ShouldBe("cif");
    }

    /// <summary>Fiecare corecție e necesară: fără ea, XML-ul acceptat de DUKIntegrator pică XSD-ul oficial.</summary>
    [Theory]
    [InlineData(DeclarationType.D100, "v2-20220224", AnafSchemaCorrections.D100V2, "cod_oblig")]
    [InlineData(DeclarationType.D301, "v1-20200130", AnafSchemaCorrections.D301V1, "temei")]
    [InlineData(DeclarationType.D390, "v3-20210212", AnafSchemaCorrections.D390V3, "operatie")]
    public void Each_schema_correction_is_needed(DeclarationType type, string version, string xsd, string field)
    {
        byte[] xml = _service.Build(version, AnafTestSupport.Input(type));

        AnafDeclarationXmlService.Validate(AnafSchemas.Load(xsd, corrected: false), xml).ShouldHaveSingleItem().Field.ShouldBe(field);
        AnafDeclarationXmlService.Validate(AnafSchemas.Load(xsd), xml).ShouldBeEmpty();
    }

    [Fact]
    public void All_schemas_are_embedded()
    {
        AnafSchemas.Paths.ShouldBe([AnafSchemaCorrections.D100V2, AnafSchemaCorrections.D301V1, AnafSchemaCorrections.D390V3], ignoreOrder: true);
        AnafSchemaCorrections.All.ShouldAllBe(correction => AnafSchemas.Exists(correction.XsdPath));
    }

    [Fact]
    public void Seeded_schemas_have_their_xsd_and_mapper()
    {
        InsertDataOperation seed = new AddAnafSchemas().UpOperations.OfType<InsertDataOperation>().ShouldHaveSingleItem();

        Enumerable.Range(0, seed.Values.GetLength(0)).Select(row => (Type: (string)seed.Values[row, 1]!, Version: (string)seed.Values[row, 2]!, Xsd: (string)seed.Values[row, 3]!))
            .ShouldAllBe(schema => _service.Supports(Enum.Parse<DeclarationType>(schema.Type), schema.Version) && AnafSchemas.Exists(schema.Xsd));
        seed.Values.GetLength(0).ShouldBe(3);
    }

    [Theory]
    [InlineData("10301010912251012000029")] // exemplul din structura D301
    [InlineData("10604010826250926000052")] // D100 acceptat de DUKIntegrator (fixture-ul serviciului Java)
    public void Evidence_number_checksum_matches_the_anaf_examples(string example) =>
        AnafFormat.IsValidEvidenceNumber(example).ShouldBeTrue();

    [Theory]
    [InlineData("București, Ștefan cel Mare", "Bucuresti, Stefan cel Mare")]
    [InlineData("Bolt Operations OÜ", "Bolt Operations OU")]
    [InlineData("  Ţară   îngustă ", "Tara ingusta")]
    public void Text_drops_diacritics(string value, string expected) => AnafFormat.Text(value).ShouldBe(expected);

    [Fact]
    public void Operator_name_keeps_only_the_allowed_characters() =>
        AnafFormat.OperatorName("Uber B.V. (Amsterdam) & Co, Ltd").ShouldBe("Uber B.V. Amsterdam Co Ltd");

    private XElement Build(DeclarationType type, string version) =>
        XDocument.Parse(Encoding.UTF8.GetString(_service.Build(version, AnafTestSupport.Input(type)))).Root!;
}

using System.Text.Json;
using Application.Abstractions.Services;
using Infrastructure.Invoicing;
using Shouldly;
using Xunit;

namespace UnitTests.Invoicing;

/// <summary>
/// Parsarea răspunsului de la Oblio e singura parte a integrării care se poate verifica fără un
/// cont real — restul e transport HTTP.
///
/// Merită verificată tocmai pentru că API-ul lor nu e uniform: numerele vin când ca numere, când
/// ca șiruri, clientul când ca obiect, când ca text, iar câmpurile opționale lipsesc cu totul în
/// loc să fie `null`. O factură parsată greșit devine o sumă greșită pe ecran.
/// </summary>
public class OblioInvoiceParserTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Fact]
    public void ParsesACompleteInvoice()
    {
        OwnerInvoice? invoice = OblioInvoiceParser.Parse(Json("""
        {
          "seriesName": "RMS",
          "number": "128",
          "issueDate": "2026-08-15",
          "dueDate": "2026-08-29",
          "total": 1842.27,
          "collected": 1842.27,
          "link": "https://oblio.eu/docs/x",
          "canceled": false,
          "client": { "name": "BOLT SERVICES RO", "cif": "RO18509931" }
        }
        """));

        invoice.ShouldNotBeNull();
        invoice!.SeriesName.ShouldBe("RMS");
        invoice.Number.ShouldBe("128");
        invoice.IssueDate.ShouldBe(new DateOnly(2026, 8, 15));
        invoice.DueDate.ShouldBe(new DateOnly(2026, 8, 29));
        invoice.TotalLei.ShouldBe(1842.27m);
        invoice.CollectedLei.ShouldBe(1842.27m);
        invoice.ClientName.ShouldBe("BOLT SERVICES RO");
        invoice.ClientCif.ShouldBe("RO18509931");
        invoice.Canceled.ShouldBeFalse();
    }

    /// <summary>Sumele ca șiruri sunt la fel de frecvente ca cele numerice.</summary>
    [Fact]
    public void ReadsAmountsWrittenAsStrings()
    {
        OwnerInvoice? invoice = OblioInvoiceParser.Parse(Json("""
        {"seriesName":"RMS","number":"7","issueDate":"2026-08-01","total":"1215.60","collected":"0"}
        """));

        invoice!.TotalLei.ShouldBe(1215.60m);
        invoice.CollectedLei.ShouldBe(0m);
    }

    /// <summary>Numărul poate veni ca număr; îl vrem tot ca text, fiindcă face parte din identitate.</summary>
    [Fact]
    public void ReadsNumericInvoiceNumber()
    {
        OwnerInvoice? invoice = OblioInvoiceParser.Parse(Json("""
        {"seriesName":"RMS","number":128,"issueDate":"2026-08-15","total":10}
        """));

        invoice!.Number.ShouldBe("128");
    }

    [Fact]
    public void AcceptsRomanianDateFormat()
    {
        OwnerInvoice? invoice = OblioInvoiceParser.Parse(Json("""
        {"seriesName":"RMS","number":"9","issueDate":"15.08.2026","total":10}
        """));

        invoice!.IssueDate.ShouldBe(new DateOnly(2026, 8, 15));
    }

    [Fact]
    public void ReadsClientGivenAsAPlainString()
    {
        OwnerInvoice? invoice = OblioInvoiceParser.Parse(Json("""
        {"seriesName":"RMS","number":"9","issueDate":"2026-08-15","total":10,"client":"UBER B.V."}
        """));

        invoice!.ClientName.ShouldBe("UBER B.V.");
        invoice.ClientCif.ShouldBeNull();
    }

    [Fact]
    public void MissingOptionalFieldsFallBackInsteadOfThrowing()
    {
        OwnerInvoice? invoice = OblioInvoiceParser.Parse(Json("""
        {"seriesName":"RMS","number":"9","issueDate":"2026-08-15"}
        """));

        invoice.ShouldNotBeNull();
        invoice!.TotalLei.ShouldBe(0m);
        invoice.CollectedLei.ShouldBe(0m);
        invoice.DueDate.ShouldBeNull();
        invoice.Link.ShouldBeNull();
        invoice.ClientName.ShouldBe("Client necunoscut");
    }

    /// <summary>Anularea are trei forme în răspunsurile lor; toate înseamnă același lucru.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("\"1\"", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void ReadsCancelledFlagInEveryShape(string raw, bool expected)
    {
        OwnerInvoice? invoice = OblioInvoiceParser.Parse(Json($$"""
        {"seriesName":"RMS","number":"9","issueDate":"2026-08-15","total":10,"canceled":{{raw}}}
        """));

        invoice!.Canceled.ShouldBe(expected);
    }

    /// <summary>
    /// Fără serie, număr sau dată, factura nu e adresabilă în Oblio: o sărim, în loc s-o afișăm
    /// pe jumătate și să eșuăm abia când cineva apasă „încasează".
    /// </summary>
    [Theory]
    [InlineData("""{"number":"9","issueDate":"2026-08-15"}""")]
    [InlineData("""{"seriesName":"RMS","issueDate":"2026-08-15"}""")]
    [InlineData("""{"seriesName":"RMS","number":"9"}""")]
    [InlineData("""{"seriesName":"RMS","number":"9","issueDate":"nu-i o dată"}""")]
    public void SkipsUnusableEntries(string raw)
    {
        OblioInvoiceParser.Parse(Json(raw)).ShouldBeNull();
    }
}

using Application.Abstractions.Ai;
using Application.Payments.ServiceOrders;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Payments;

/// <summary>
/// Buletinul din formularul serviciilor de pe site precompletează datele personale. Ce nu trece
/// validatorul (un CNP greșit) rămâne gol, iar citirea e plafonată pe IP.
/// </summary>
public sealed class ScanIdentityCardTests
{
    [Fact]
    public async Task Buletinul_citit_precompleteaza_formularul()
    {
        var analyzer = new FakeAnalyzer(Fields(
            ("nume", "POPESCU"),
            ("prenume", "ION"),
            ("cnp", "501 051 942 0017"),
            ("serie_act", "if"),
            ("numar_act", "123456"),
            ("autoritate_emitenta", "SPCLEP Voluntari"),
            ("data_emiterii", "2020-01-10"),
            ("data_expirarii", "2030-01-10"),
            ("domiciliu_judet", "Ilfov"),
            ("domiciliu_localitate", "Voluntari"),
            ("domiciliu_strada", "Pipera"),
            ("domiciliu_numar", "12")));

        Result<IdentityCardScan> result = await Handler(analyzer, new FakeLimiter()).Handle(Command(), CancellationToken.None);

        IdentityCardScan scan = result.Value;
        scan.Nume.ShouldBe("POPESCU");
        scan.Cnp.ShouldBe("5010519420017");
        scan.SerieAct.ShouldBe("IF");
        scan.DataExpirarii.ShouldBe(new DateOnly(2030, 1, 10));
        scan.Domiciliu.Localitate.ShouldBe("Voluntari");
        scan.PrefilledFields.ShouldContain("CNP");
        scan.PrefilledFields.ShouldContain("DOMICILIU_STRADA");
        scan.Note.ShouldBeNull();
    }

    [Fact]
    public async Task CNP_cu_cifra_de_control_gresita_nu_se_precompleteaza()
    {
        var analyzer = new FakeAnalyzer(Fields(("nume", "POPESCU"), ("prenume", "ION"), ("cnp", "5010519420010")));

        IdentityCardScan scan = (await Handler(analyzer, new FakeLimiter()).Handle(Command(), CancellationToken.None)).Value;

        scan.Cnp.ShouldBeNull();
        scan.PrefilledFields.ShouldNotContain("CNP");
        scan.Note.ShouldNotBeNull();
    }

    [Fact]
    public async Task Numele_intreg_se_desparte_cand_lipsesc_cele_separate()
    {
        var analyzer = new FakeAnalyzer(Fields(("full_name", "POPESCU ION ANDREI")));

        IdentityCardScan scan = (await Handler(analyzer, new FakeLimiter()).Handle(Command(), CancellationToken.None)).Value;

        scan.Nume.ShouldBe("POPESCU");
        scan.Prenume.ShouldBe("ION ANDREI");
        scan.PrefilledFields.ShouldNotContain("FULL_NAME");
    }

    [Fact]
    public async Task Alt_document_spune_de_ce_nu_s_a_completat_nimic()
    {
        var analyzer = new FakeAnalyzer([], matches: false);

        IdentityCardScan scan = (await Handler(analyzer, new FakeLimiter()).Handle(Command(), CancellationToken.None)).Value;

        scan.PrefilledFields.ShouldBeEmpty();
        scan.Note!.ShouldContain("act de identitate");
    }

    [Fact]
    public async Task Plafonul_pe_IP_opreste_apelul_la_model()
    {
        var analyzer = new FakeAnalyzer([]);

        Result<IdentityCardScan> result = await Handler(analyzer, new FakeLimiter(allow: false)).Handle(Command(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("IdentityScan.Limit");
        analyzer.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Tipul_de_fisier_necunoscut_e_refuzat()
    {
        var analyzer = new FakeAnalyzer([]);

        Result<IdentityCardScan> result = await Handler(analyzer, new FakeLimiter())
            .Handle(Command() with { ContentType = "application/zip" }, CancellationToken.None);

        result.Error.Code.ShouldBe("IdentityScan.InvalidType");
        analyzer.Calls.ShouldBe(0);
    }

    private static ScanIdentityCardCommand Command() =>
        new("buletin.jpg", new MemoryStream([1, 2, 3]), "image/jpeg", 3, "81.0.0.1");

    private static ScanIdentityCardCommandHandler Handler(FakeAnalyzer analyzer, FakeLimiter limiter) => new(analyzer, limiter);

    private static List<AiFieldResult> Fields(params (string Key, string Value)[] values) =>
        values.Select(v => new AiFieldResult(v.Key, v.Value, 0.9)).ToList();

    private sealed class FakeAnalyzer(List<AiFieldResult> fields, bool matches = true) : IDocumentAiAnalyzer
    {
        public int Calls { get; private set; }

        public Task<Result<DocumentAiAnalysisResult>> AnalyzeAsync(DocumentAiAnalysisRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result.Success(new DocumentAiAnalysisResult(
                matches, true, null, null, matches ? "Carte de identitate" : "Factură", string.Empty, fields, 0.9)));
        }
    }

    private sealed class FakeLimiter(bool allow = true) : IAiUsageLimiter
    {
        public bool TryConsume(Guid userId, string feature, int maxCalls, TimeSpan window) => allow;
    }
}

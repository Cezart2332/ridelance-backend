using System.Net;
using System.Text;
using Application.Abstractions.Services;
using Infrastructure.Banking;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

// Handlerul și clientul HTTP din teste trăiesc cât testul și nu țin nimic nativ: analizorul cere
// `using`, dar aici ar adăuga un nivel de imbricare la fiecare test fără să elibereze nimic real.
#pragma warning disable CA2000

namespace UnitTests.Banking;

/// <summary>
/// Furnizorul de open banking, pe răspunsuri de formă reală.
///
/// Ce se verifică aici sunt exact lucrurile care nu se văd la citirea codului: plicul
/// `{status, messageStatus, payload}` în care vine orice răspuns, tokenurile emise per
/// consimțământ și rotația lor, și faptul că un acord mort ajunge la aplicație ca excepția pe care
/// jobul de sincronizare o tratează — nu ca o listă goală de tranzacții.
/// </summary>
public sealed class SmartAccountsProviderTests
{
    private const string TokenPayload = """
        {"status":200,"messageStatus":"Success","payload":{
            "internalConsentId":4211,"access_token":"acc-1","refresh_token":"ref-1"}}
        """;

    [Fact]
    public async Task ListInstitutions_ReadsTheParametersEachBankAsksForUpFront()
    {
        StubHandler handler = new StubHandler()
            .Enqueue(TokenPayload)
            .Enqueue("""
                {"status":200,"messageStatus":"Success","payload":[
                    {"code":"BT","name":"Banca Transilvania","logo":"bt.png","bic":"BTRLRO22","active":true,
                     "requiredParameters":{"requiresPSUId":false,"requiresPSUIdType":false,"requiresIban":false}},
                    {"code":"BRD","name":"BRD","active":true,
                     "requiredParameters":{"requiresPSUId":true,"requiresPSUIdType":true,"requiresIban":false}},
                    {"code":"OLD","name":"Bancă retrasă","active":false,"requiredParameters":{}}]}
                """);

        IReadOnlyList<BankInstitutionInfo> banks = await Provider(handler).ListInstitutionsAsync();

        // Banca inactivă nu are ce căuta în selector: utilizatorul ar alege-o și ar primi eroare.
        banks.Count.ShouldBe(2);

        BankInstitutionInfo brd = banks.Single(b => b.Id == "BRD");
        brd.RequiresPsuId.ShouldBeTrue();
        brd.RequiresPsuIdType.ShouldBeTrue();

        banks.Single(b => b.Id == "BT").RequiresPsuId.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateConsent_ReturnsTheBankAddressAndTheTokensThatOpenIt()
    {
        StubHandler handler = new StubHandler()
            .Enqueue(TokenPayload)
            .Enqueue("""
                {"status":200,"messageStatus":"Success","payload":{
                    "consentStatus":"received","consentId":"c-99",
                    "_links":{"scaOAuth":{"href":"https://banca.example/authorize?x=1"}}}}
                """);

        BankConsentCreated consent = await Provider(handler).CreateConsentAsync(
            new BankConsentRequest("BT", "sofer@example.test", "user-1", "https://ridelance.ro/banca/retur"));

        // Identificatorul e cel de la autentificare, nu `consentId`-ul băncii: apelurile
        // următoare merg pe el.
        consent.ConsentId.ShouldBe("4211");
        consent.AuthorizationAddress.ShouldBe("https://banca.example/authorize?x=1");
        consent.Tokens.AccessToken.ShouldBe("acc-1");
        consent.Tokens.RefreshToken.ShouldBe("ref-1");
        consent.Tokens.AccessExpiresAtUtc.ShouldBeGreaterThan(DateTime.UtcNow);

        handler.Requests[1].RequestUri!.ToString().ShouldEndWith("/initConsent/4211");
    }

    [Fact]
    public async Task ExpiredConsent_SurfacesAsTheExceptionTheSyncJobUnderstands()
    {
        // Un acord mort vine cu 200 și cu starea în corp. Fără traducerea asta, sincronizarea ar
        // raporta „mers, zero tranzacții" pentru o bancă la care nu mai avem acces.
        StubHandler handler = new StubHandler().Enqueue("""
            {"status":200,"messageStatus":"Success","payload":{"consentStatus":"revokedByPsu"}}
            """);

        var store = new StubTokenStore(new BankConsentTokens("acc-1", "ref-1", DateTime.UtcNow.AddMinutes(4)));

        await Should.ThrowAsync<BankDataConsentExpiredException>(
            () => Provider(handler, store).GetConsentAsync("BT", "4211"));
    }

    [Fact]
    public async Task ExpiredAccessToken_IsRenewedAndTheRotationIsSavedBeforeTheCall()
    {
        StubHandler handler = new StubHandler()
            .Enqueue("""
                {"status":200,"messageStatus":"Success","payload":{
                    "internalConsentId":4211,"access_token":"acc-2","refresh_token":"ref-2"}}
                """)
            .Enqueue("""
                {"status":200,"messageStatus":"Success","payload":{"consentStatus":"valid",
                 "validUntil":"2026-12-31T00:00:00Z"}}
                """);

        // Token expirat: prima cerere trebuie să fie reînnoirea, nu interogarea.
        var store = new StubTokenStore(new BankConsentTokens("acc-1", "ref-1", DateTime.UtcNow.AddSeconds(-1)));

        BankConsentState state = await Provider(handler, store).GetConsentAsync("BT", "4211");

        state.Status.ShouldBe("valid");
        handler.Requests[0].RequestUri!.ToString().ShouldContain("refreshToken");

        // În CORP, ca formular — nu în query, cum spune OpenAPI-ul lor. Sandboxul răspunde 401 la
        // varianta din query și la cea urlencoded; doar multipartul întoarce o pereche nouă.
        handler.Requests[0].RequestUri!.Query.ShouldNotContain("refresh_token");
        handler.Bodies[0].ShouldContain("ref-1");
        handler.Bodies[0].ShouldContain("ridelance");
        handler.Requests[0].Content!.Headers.ContentType!.MediaType.ShouldBe("multipart/form-data");

        // Rotația e salvată imediat: tokenul vechi de reîmprospătare e deja invalid la ei, deci un
        // proces care ar cădea între reînnoire și final ar rămâne cu o pereche moartă.
        store.Saved!.RefreshToken.ShouldBe("ref-2");
        handler.Requests[1].Headers.Authorization!.Parameter.ShouldBe("acc-2");
    }

    [Fact]
    public async Task Transactions_AreReadFromBothListsWithTheSignTheBankImplies()
    {
        StubHandler handler = new StubHandler().Enqueue("""
            {"status":200,"messageStatus":"Success","payload":{"transactions":{
                "booked":[
                    {"transactionId":"t1","bookingDate":"2026-09-01","valueDate":"2026-09-02",
                     "transactionAmount":{"amount":"150.25","currency":"RON"},
                     "creditDebitIndicator":"DBIT","creditorName":"OMV",
                     "remittanceInformationUnstructured":"Alimentare"},
                    {"transactionId":"t2","bookingDate":"2026-09-03",
                     "transactionAmount":{"amount":"980.00","currency":"RON"},
                     "creditDebitIndicator":"CRDT","debtorName":"Uber BV"}],
                "pending":[
                    {"entryReference":"p1","bookingDate":"2026-09-04",
                     "transactionAmount":{"amount":"-10.00","currency":"RON"}}]}}}
            """);

        var store = new StubTokenStore(new BankConsentTokens("acc-1", "ref-1", DateTime.UtcNow.AddMinutes(4)));

        BankTransactionsPage page = await Provider(handler, store)
            .GetTransactionsAsync("BT", "4211", "RO49AAAA1B31007593840000", new DateOnly(2026, 8, 1), null);

        page.Booked.Count.ShouldBe(2);

        // Suma vine pozitivă, cu direcția separat: fără semn, o plată ar intra ca încasare.
        page.Booked[0].Amount.ShouldBe(-150.25m);
        page.Booked[0].CounterpartyName.ShouldBe("OMV");
        page.Booked[1].Amount.ShouldBe(980.00m);
        page.Booked[1].CounterpartyName.ShouldBe("Uber BV");

        // Nefinalizatele n-au `transactionId` la unele bănci, iar identificatorul lor se prefixează:
        // aceeași tranzacție reapare mai târziu ca finalizată, cu alt id, și n-avem voie să pierdem
        // niciuna din ele la deduplicare.
        page.Pending.Count.ShouldBe(1);
        page.Pending[0].ProviderTransactionId.ShouldBe("pending:p1");
    }

    [Fact]
    public async Task Pagination_SendsTheNextLinkBackAsAHeaderOnTheSameAddress()
    {
        StubHandler handler = new StubHandler()
            .Enqueue("""
                {"status":200,"messageStatus":"Success","payload":{"transactions":{
                    "booked":[{"transactionId":"t1","transactionAmount":{"amount":"1.00","currency":"RON"}}],
                    "links":{"next":{"href":"cursor-2"}}}}}
                """)
            .Enqueue("""
                {"status":200,"messageStatus":"Success","payload":{"transactions":{
                    "booked":[{"transactionId":"t2","transactionAmount":{"amount":"2.00","currency":"RON"}}]}}}
                """);

        var store = new StubTokenStore(new BankConsentTokens("acc-1", "ref-1", DateTime.UtcNow.AddMinutes(4)));

        BankTransactionsPage page = await Provider(handler, store)
            .GetTransactionsAsync("BT", "4211", "acct", null, null);

        page.Booked.Count.ShouldBe(2);
        handler.Requests[1].RequestUri.ShouldBe(handler.Requests[0].RequestUri);
        handler.Requests[1].Headers.GetValues("next").Single().ShouldBe("cursor-2");
    }

    [Fact]
    public async Task ProCredit_GetsBookedOnly_BecauseItRejectsAnythingElse()
    {
        StubHandler handler = new StubHandler().Enqueue("""
            {"status":200,"messageStatus":"Success","payload":{"transactions":{"booked":[]}}}
            """);

        var store = new StubTokenStore(new BankConsentTokens("acc-1", "ref-1", DateTime.UtcNow.AddMinutes(4)));

        await Provider(handler, store).GetTransactionsAsync("PCB", "4211", "acct", null, null);

        handler.Requests[0].RequestUri!.Query.ShouldContain("bookingStatus=booked");
    }

    [Fact]
    public void WithoutCertificate_TheIntegrationReportsItselfUnconfigured()
    {
        // Un mediu fără open banking configurat trebuie să meargă mai departe, nu să cadă la
        // pornire: restul platformei nu depinde de banca conectată. Certificatul e cel real, fără
        // nimic configurat — exact starea unui mediu de test sau a unui deploy nou.
        var options = new SmartAccountsOptions { ClientId = "ridelance" };

        var provider = new SmartAccountsBankDataProvider(
            new HttpClient(new StubHandler()),
            Options.Create(options),
            new SmartAccountsCertificate(Options.Create(options)),
            new StubTokenStore(null));

        provider.IsConfigured.ShouldBeFalse();
    }

    private static SmartAccountsBankDataProvider Provider(
        StubHandler handler,
        IBankConsentTokenStore? tokenStore = null)
    {
        var options = new SmartAccountsOptions
        {
            ClientId = "ridelance",
            ApiBaseUrl = "https://api.test",
            MtlsBaseUrl = "https://mtls.test",
        };

        return new SmartAccountsBankDataProvider(
            new HttpClient(handler),
            Options.Create(options),
            new StubCertificate(options),
            tokenStore ?? new StubTokenStore(null));
    }

    /// <summary>Certificat prezent, fără să existe unul pe disc — altfel toate apelurile ar fi respinse.</summary>
    private sealed class StubCertificate(SmartAccountsOptions options)
        : SmartAccountsCertificate(Options.Create(options))
    {
        public override bool IsPresent => true;
    }

    private sealed class StubTokenStore(BankConsentTokens? initial) : IBankConsentTokenStore
    {
        private BankConsentTokens? _tokens = initial;

        public BankConsentTokens? Saved { get; private set; }

        public Task<BankConsentTokens?> GetAsync(string consentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_tokens);

        public Task SaveAsync(string consentId, BankConsentTokens tokens, CancellationToken cancellationToken = default)
        {
            _tokens = tokens;
            Saved = tokens;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        public StubHandler Enqueue(string json)
        {
            _responses.Enqueue(json);
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            // Corpul se citește acum: după ce pleacă răspunsul, conținutul e deja eliberat.
            Bodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responses.Count > 0 ? _responses.Dequeue() : "{}",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}

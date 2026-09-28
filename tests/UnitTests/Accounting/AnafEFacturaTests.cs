using System.IO.Compression;
using System.Net;
using System.Text;
using Application.Abstractions.Anaf;
using Application.Abstractions.Authentication;
using Application.Accounting.Anaf;
using Application.Accounting.Declarations;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Accounting.Anaf;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

#pragma warning disable CA2000 // Handler-ul și HttpClient-ul trăiesc cât testul.

namespace UnitTests.Accounting;

/// <summary>
/// Conexiunea ANAF (OAuth cu certificatul împuternicitului) și e-Factura: clientul HTTP pe forma
/// răspunsurilor ANAF, apoi conectarea, reînnoirea tokenului și sincronizarea unui PFA.
/// </summary>
public sealed class AnafEFacturaTests : IDisposable
{
    private const string Cui = "12345674";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly FakeAnaf _anaf = new();
    private readonly MemoryFiles _files = new();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _pfa;

    public AnafEFacturaTests()
    {
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance", Role = UserRole.Admin });
        var user = new User { Id = Guid.NewGuid(), Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu", Role = UserRole.Client };
        var pfa = new PfaRegistration { Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Ion Popescu", LegalName = "POPESCU ION PFA", Cui = Cui };
        _pfa = pfa.Id;
        _db.PfaRegistrations.Add(pfa);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ---- clientul HTTP ----

    [Fact]
    public void The_authorize_url_asks_for_a_jwt_code_on_the_registered_callback()
    {
        Uri url = HttpClient(new Scripted()).AuthorizeUrl("abc");

        url.GetLeftPart(UriPartial.Path).ShouldBe("https://logincert.anaf.ro/anaf-oauth2/v1/authorize");
        url.Query.ShouldContain("response_type=code");
        url.Query.ShouldContain("client_id=client-1");
        url.Query.ShouldContain("redirect_uri=https%3A%2F%2Fapi.ridelance.ro%2Fanaf%2Foauth%2Fcallback");
        url.Query.ShouldContain("token_content_type=jwt");
        url.Query.ShouldContain("state=abc");
    }

    [Fact]
    public async Task The_code_is_exchanged_with_basic_auth_and_the_jwt_expiry_is_read()
    {
        long exp = new DateTimeOffset(2026, 12, 27, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var handler = new Scripted((HttpStatusCode.OK, $$"""{"access_token":"{{Jwt(exp)}}","refresh_token":"r-1","token_type":"Bearer"}"""));

        AnafTokens tokens = (await HttpClient(handler).ExchangeCodeAsync("code-1", CancellationToken.None)).Value;

        tokens.RefreshToken.ShouldBe("r-1");
        tokens.AccessExpiresAtUtc.ShouldBe(new DateTime(2026, 12, 27, 0, 0, 0, DateTimeKind.Utc));
        Sent sent = handler.Requests.ShouldHaveSingleItem();
        sent.Authorization.ShouldBe($"Basic {Convert.ToBase64String("client-1:secret-1"u8.ToArray())}");
        sent.Body.ShouldContain("grant_type=authorization_code");
        sent.Body.ShouldContain("code=code-1");
        sent.Body.ShouldContain("redirect_uri=https%3A%2F%2Fapi.ridelance.ro%2Fanaf%2Foauth%2Fcallback");
        sent.Body.ShouldContain("token_content_type=jwt");
    }

    [Fact]
    public async Task The_message_list_is_read_and_no_messages_is_not_an_error()
    {
        var handler = new Scripted(
            (HttpStatusCode.OK, """
                {"mesaje":[{"data_creare":"202609151430","cif":"12345674","id_solicitare":"5001","detalii":"Factura cu id_incarcare=5001 emisa de cif_emitent=1590082 pentru cif_beneficiar=12345674","tip":"FACTURA PRIMITA","id":"3001"}],
                 "numar_total_pagini":1,"serial":"x","cui":"12345674","titlu":"Lista Mesaje"}
                """),
            (HttpStatusCode.OK, """{"eroare":"Nu exista mesaje in intervalul selectat","titlu":"Lista Mesaje"}"""),
            (HttpStatusCode.OK, """{"eroare":"Nu aveti drept de inrolare pentru CIF=12345674","titlu":"Lista Mesaje"}"""));
        AnafEFacturaClient client = HttpClient(handler);
        DateTime from = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime to = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);

        EFacturaPage page = (await client.ListMessagesAsync("t", Cui, from, to, 1, CancellationToken.None)).Value;
        EFacturaListItem message = page.Messages.ShouldHaveSingleItem();
        message.Id.ShouldBe("3001");
        message.Type.ShouldBe("FACTURA PRIMITA");
        message.CreatedAtUtc.ShouldBe(new DateTime(2026, 9, 15, 11, 30, 0, DateTimeKind.Utc));
        handler.Requests[0].Uri.ShouldBe(
            $"https://api.anaf.ro/prod/FCTEL/rest/listaMesajePaginatieFactura?startTime={new DateTimeOffset(from).ToUnixTimeMilliseconds()}&endTime={new DateTimeOffset(to).ToUnixTimeMilliseconds()}&cif=12345674&pagina=1");
        handler.Requests[0].Authorization.ShouldBe("Bearer t");

        EFacturaPage empty = (await client.ListMessagesAsync("t", Cui, from, to, 1, CancellationToken.None)).Value;
        empty.Messages.ShouldBeEmpty();
        empty.Error.ShouldBeNull();

        (await client.ListMessagesAsync("t", Cui, from, to, 1, CancellationToken.None)).Value.Error.ShouldBe("Nu aveti drept de inrolare pentru CIF=12345674");
    }

    [Fact]
    public async Task A_download_that_is_not_a_zip_is_the_anaf_error()
    {
        var handler = new Scripted((HttpStatusCode.OK, """{"eroare":"Pentru id=3001 nu exista inregistrata nici o factura"}"""));

        Result<byte[]> zip = await HttpClient(handler).DownloadAsync("t", "3001", CancellationToken.None);

        zip.Error.Description.ShouldBe("ANAF: Pentru id=3001 nu exista inregistrata nici o factura");
    }

    // ---- conexiunea și sincronizarea ----

    [Fact]
    public async Task The_callback_saves_encrypted_tokens_for_the_admin_who_started_it()
    {
        AnafEFacturaService service = Service();
        string url = (await service.StartAsync(_admin, "/admin?tab=contab_pfa&pfa=1", CancellationToken.None)).Value;
        string state = (await _db.AnafAuthorizationRequests.SingleAsync()).State;
        url.ShouldContain($"state={state}");

        (string path, Result outcome) = await service.CompleteAsync("code-1", state, null, CancellationToken.None);

        outcome.IsSuccess.ShouldBeTrue();
        path.ShouldBe("/admin?tab=contab_pfa&pfa=1");
        AnafConnection connection = await _db.AnafConnections.SingleAsync();
        connection.UserId.ShouldBe(_admin);
        connection.AccessTokenProtected.ShouldBe("enc:access-1");
        (await service.AccessTokenAsync(CancellationToken.None)).Value.ShouldBe("access-1");

        // Un al doilea callback cu același state nu mai e valid.
        (await service.CompleteAsync("code-2", state, null, CancellationToken.None)).Outcome.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_state_the_latest_authorization_is_used_and_the_access_token_is_refreshed_before_it_expires()
    {
        AnafEFacturaService service = Service();
        await service.StartAsync(_admin, "/admin", CancellationToken.None);
        (await service.CompleteAsync("code-1", null, null, CancellationToken.None)).Outcome.IsSuccess.ShouldBeTrue();

        AnafConnection connection = await _db.AnafConnections.SingleAsync();
        connection.AccessExpiresAtUtc = DateTime.UtcNow.AddDays(3);
        await _db.SaveChangesAsync();

        (await service.AccessTokenAsync(CancellationToken.None)).Value.ShouldBe("access-2");
        connection.RefreshTokenProtected.ShouldBe("enc:refresh-2");
        _anaf.Refreshes.ShouldBe(["refresh-1"]);
    }

    [Fact]
    public async Task A_client_without_spv_rights_stays_with_the_anaf_reason()
    {
        AnafEFacturaService service = await Connected();
        _anaf.ListError = "Nu aveti drept de inrolare pentru CIF=12345674";

        AnafPfaLink link = (await service.ConnectPfaAsync(_pfa, _admin, CancellationToken.None)).Value;

        link.Status.ShouldBe(AnafPfaLinkStatus.NoAccess);
        link.LastError.ShouldBe("Nu aveti drept de inrolare pentru CIF=12345674");
        (await _db.EFacturaMessages.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Connecting_syncs_the_invoices_once_and_reads_them_from_the_xml()
    {
        AnafEFacturaService service = await Connected();
        _anaf.Messages.Add(new EFacturaListItem("3001", "FACTURA PRIMITA", new DateTime(2026, 9, 15, 11, 30, 0, DateTimeKind.Utc), "5001", Cui, "Factura OMV"));
        _anaf.Messages.Add(new EFacturaListItem("3002", "ERORI FACTURA", new DateTime(2026, 9, 16, 8, 0, 0, DateTimeKind.Utc), "5002", Cui, "Erori"));

        (await service.ConnectPfaAsync(_pfa, _admin, CancellationToken.None)).Value.Status.ShouldBe(AnafPfaLinkStatus.Active);

        EFacturaMessage invoice = await _db.EFacturaMessages.SingleAsync(m => m.AnafMessageId == "3001");
        invoice.Kind.ShouldBe(EFacturaMessageKind.Received);
        invoice.InvoiceNumber.ShouldBe("OMV-2026-0915");
        invoice.IssueDate.ShouldBe(new DateOnly(2026, 9, 15));
        invoice.SupplierName.ShouldBe("OMV PETROM MARKETING SRL");
        invoice.SupplierCif.ShouldBe("RO11201891");
        invoice.CustomerCif.ShouldBe(Cui);
        invoice.TotalAmount.ShouldBe(363m);
        invoice.VatAmount.ShouldBe(63m);
        invoice.ZipDocumentId.ShouldNotBeNull();
        (await _db.EFacturaMessages.SingleAsync(m => m.AnafMessageId == "3002")).Kind.ShouldBe(EFacturaMessageKind.Error);

        // A doua sincronizare nu dublează și nu mai descarcă.
        EFacturaSyncResult again = (await service.SyncAsync(_pfa, CancellationToken.None)).Value;
        again.ShouldBe(new EFacturaSyncResult(0, 0));
        (await _db.EFacturaMessages.CountAsync()).ShouldBe(2);
        _anaf.Downloads.ShouldBe(["3001", "3002"]);

        // PDF-ul: generat o dată de ANAF, apoi din arhivă.
        (await service.PdfAsync(invoice.Id, CancellationToken.None)).Value.FileName.ShouldBe("efactura_OMV-2026-0915.pdf");
        await service.PdfAsync(invoice.Id, CancellationToken.None);
        _anaf.Pdfs.ShouldBe(1);
    }

    [Fact]
    public async Task Without_an_anaf_connection_a_client_cannot_be_connected()
    {
        (await Service().ConnectPfaAsync(_pfa, _admin, CancellationToken.None)).Error.ShouldBe(AnafIntegrationErrors.NotConnected);
    }

    private async Task<AnafEFacturaService> Connected()
    {
        AnafEFacturaService service = Service();
        await service.StartAsync(_admin, "/admin", CancellationToken.None);
        await service.CompleteAsync("code-1", null, null, CancellationToken.None);
        return service;
    }

    private AnafEFacturaService Service() =>
        new(_db, _anaf, new PrefixSecrets(), new DeclarationFiles(_db, new AnafDeclarationXmlService(), _files, new PlainSecrets()), NullLogger<AnafEFacturaService>.Instance);

    private static AnafEFacturaClient HttpClient(Scripted handler) => new(
        new HttpClient(handler),
        Options.Create(new AnafOAuthOptions { ClientId = "client-1", ClientSecret = "secret-1" }),
        NullLogger<AnafEFacturaClient>.Instance);

    private static string Jwt(long exp)
    {
        static string Part(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part("""{"alg":"RS512"}""")}.{Part($$"""{"exp":{{exp}},"sub":"x"}""")}.semnatura";
    }

    internal static byte[] Zip(string invoiceXml)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in new[] { ("5001.xml", invoiceXml), ("semnatura_5001.xml", "<Signature/>") })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private const string Invoice = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:ID>OMV-2026-0915</cbc:ID>
          <cbc:IssueDate>2026-09-15</cbc:IssueDate>
          <cbc:DocumentCurrencyCode>RON</cbc:DocumentCurrencyCode>
          <cac:AccountingSupplierParty><cac:Party>
            <cac:PartyTaxScheme><cbc:CompanyID>RO11201891</cbc:CompanyID></cac:PartyTaxScheme>
            <cac:PartyLegalEntity><cbc:RegistrationName>OMV PETROM MARKETING SRL</cbc:RegistrationName></cac:PartyLegalEntity>
          </cac:Party></cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty><cac:Party>
            <cac:PartyLegalEntity><cbc:RegistrationName>POPESCU ION PFA</cbc:RegistrationName><cbc:CompanyID>12345674</cbc:CompanyID></cac:PartyLegalEntity>
          </cac:Party></cac:AccountingCustomerParty>
          <cac:TaxTotal><cbc:TaxAmount currencyID="RON">63.00</cbc:TaxAmount></cac:TaxTotal>
          <cac:LegalMonetaryTotal><cbc:PayableAmount currencyID="RON">363.00</cbc:PayableAmount></cac:LegalMonetaryTotal>
        </Invoice>
        """;

    /// <summary>ANAF simulat: o conexiune, o listă de mesaje, arhive cu factura de mai sus.</summary>
    private sealed class FakeAnaf : IAnafEFacturaClient
    {
        private int _refreshes;

        public List<EFacturaListItem> Messages { get; } = [];
        public string? ListError { get; set; }
        public List<string> Refreshes { get; } = [];
        public List<string> Downloads { get; } = [];
        public int Pdfs { get; private set; }

        public bool IsConfigured => true;

        public Uri AuthorizeUrl(string state) => new($"https://logincert.anaf.ro/anaf-oauth2/v1/authorize?state={state}");

        public Task<Result<AnafTokens>> ExchangeCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult<Result<AnafTokens>>(new AnafTokens("access-1", "refresh-1", DateTime.UtcNow.AddDays(90), DateTime.UtcNow.AddDays(365)));

        public Task<Result<AnafTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            Refreshes.Add(refreshToken);
            _refreshes++;
            return Task.FromResult<Result<AnafTokens>>(new AnafTokens($"access-{_refreshes + 1}", $"refresh-{_refreshes + 1}", DateTime.UtcNow.AddDays(90), DateTime.UtcNow.AddDays(365)));
        }

        public Task<Result<EFacturaPage>> ListMessagesAsync(string accessToken, string cif, DateTime fromUtc, DateTime toUtc, int page, CancellationToken cancellationToken) =>
            Task.FromResult<Result<EFacturaPage>>(new EFacturaPage(ListError is null ? [.. Messages] : [], 1, ListError));

        public Task<Result<byte[]>> DownloadAsync(string accessToken, string messageId, CancellationToken cancellationToken)
        {
            Downloads.Add(messageId);
            string xml = messageId == "3001" ? Invoice : """<header xmlns="mfp:anaf:dgti:efactura:mesajEroriFactuta:v1"><Error errorMessage="E"/></header>""";
            return Task.FromResult<Result<byte[]>>(Zip(xml));
        }

        public Task<Result<byte[]>> ToPdfAsync(byte[] invoiceXml, bool creditNote, CancellationToken cancellationToken)
        {
            Pdfs++;
            return Task.FromResult<Result<byte[]>>("%PDF-1.4 factura"u8.ToArray());
        }
    }

    /// <summary>„Criptare” vizibilă: testul verifică că tokenurile nu se salvează în clar.</summary>
    private sealed class PrefixSecrets : Application.Abstractions.Security.ISecretProtector
    {
        public string Protect(string plainText) => $"enc:{plainText}";

        public string Unprotect(string protectedText) => protectedText["enc:".Length..];
    }

    private sealed record Sent(string Uri, string? Authorization, string Body);

    private sealed class Scripted(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Sent(
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            (HttpStatusCode status, string body) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

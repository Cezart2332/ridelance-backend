using System.Net;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Services;
using Application.Mailboxes;
using Infrastructure.Mailboxes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace UnitTests.Mailboxes;

/// <summary>Adaptorul Migadu, pe un server HTTP simulat: ce cere, cum citește și când reîncearcă.</summary>
public sealed class MigaduMailboxProviderTests : IDisposable
{
    private readonly Server _server = new();
    private readonly HttpClient _http;
    private readonly MigaduMailboxProvider _migadu;

    public MigaduMailboxProviderTests()
    {
        _http = new HttpClient(_server);
        _migadu = new MigaduMailboxProvider(
            _http,
            Options.Create(new MailboxOptions { Provider = MailboxOptions.MigaduProvider, ApiUser = "admin@ridelance.ro", ApiKey = "cheie" }),
            NullLogger<MigaduMailboxProvider>.Instance);
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task CreateMailbox_SendsTheSpecifiedFlags_WithBasicAuth()
    {
        _server.Respond(HttpStatusCode.OK, """{"address":"ion.popescu@pfa.ridelance.ro","local_part":"ion.popescu"}""");

        MailboxResult created = await _migadu.CreateMailboxAsync(new CreateMailboxRequest("Ion Popescu", "ion.popescu", "parola"), default);

        created.Address.ShouldBe("ion.popescu@pfa.ridelance.ro");
        Call call = _server.Calls.ShouldHaveSingleItem();
        (call.Method, call.Url).ShouldBe(("POST", "https://api.migadu.com/v1/domains/pfa.ridelance.ro/mailboxes"));
        call.Authorization.ShouldBe("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("admin@ridelance.ro:cheie")));
        call.ContentType.ShouldBe("application/json");

        JsonElement body = JsonDocument.Parse(call.Body!).RootElement;
        body.GetProperty("name").GetString().ShouldBe("Ion Popescu");
        body.GetProperty("local_part").GetString().ShouldBe("ion.popescu");
        body.GetProperty("password").GetString().ShouldBe("parola");
        body.GetProperty("may_send").GetBoolean().ShouldBeTrue();
        body.GetProperty("may_receive").GetBoolean().ShouldBeTrue();
        body.GetProperty("may_access_imap").GetBoolean().ShouldBeTrue();
        body.GetProperty("may_access_pop3").GetBoolean().ShouldBeFalse();
        body.GetProperty("may_access_managesieve").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task CreateIdentity_HasItsOwnPassword_AndImapAccess()
    {
        _server.Respond(HttpStatusCode.OK, """{"address":"rid-ops-ion.popescu@pfa.ridelance.ro"}""");

        await _migadu.CreateIdentityAsync("ion.popescu", new CreateIdentityRequest("RIDElance", "rid-ops-ion.popescu", "alta"), default);

        Call call = _server.Calls.ShouldHaveSingleItem();
        (call.Method, call.Url).ShouldBe(("POST", "https://api.migadu.com/v1/domains/pfa.ridelance.ro/mailboxes/ion.popescu/identities"));
        JsonElement body = JsonDocument.Parse(call.Body!).RootElement;
        body.GetProperty("password_use").GetString().ShouldBe("custom");
        body.GetProperty("password").GetString().ShouldBe("alta");
        body.GetProperty("may_access_imap").GetBoolean().ShouldBeTrue();
        body.GetProperty("may_send").GetBoolean().ShouldBeTrue();
        body.GetProperty("may_access_pop3").GetBoolean().ShouldBeFalse();
        body.GetProperty("may_access_managesieve").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Updates_SendOnlyTheChangedField()
    {
        _server.Respond(HttpStatusCode.OK, "{}");
        _server.Respond(HttpStatusCode.OK, "{}");

        await _migadu.SetMailboxPasswordAsync("ion.popescu", "noua", default);
        await _migadu.SetRecoveryEmailAsync("ion.popescu", "ion@gmail.com", default);

        _server.Calls.Select(c => $"{c.Method} {c.Url}").Distinct()
            .ShouldBe(["PUT https://api.migadu.com/v1/domains/pfa.ridelance.ro/mailboxes/ion.popescu"]);
        _server.Calls[0].Body.ShouldBe("""{"password":"noua"}""");
        _server.Calls[1].Body.ShouldBe("""{"password_recovery_email":"ion@gmail.com"}""");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task MailboxExists_ReadsTheStatus(HttpStatusCode status, bool exists)
    {
        _server.Respond(status, "{}");

        (await _migadu.MailboxExistsAsync("ion.popescu", default)).ShouldBe(exists);
        _server.Calls.ShouldHaveSingleItem().Method.ShouldBe("GET");
    }

    [Fact]
    public async Task ARefusal_IsNeverRetried_AndKeepsThePasswordOutOfTheMessage()
    {
        _server.Respond(HttpStatusCode.BadRequest, """{"error":"local_part has already been taken"}""");

        MailboxProviderException refused = await Should.ThrowAsync<MailboxProviderException>(
            () => _migadu.CreateMailboxAsync(new CreateMailboxRequest("Ion", "ion.popescu", "parola-secreta"), default));

        _server.Calls.Count.ShouldBe(1);
        refused.Message.ShouldContain("already been taken");
        refused.Message.ShouldNotContain("parola-secreta");
    }

    [Fact]
    public async Task ServerErrors_AreRetried_ThenReported()
    {
        _server.Respond(HttpStatusCode.BadGateway, "");
        _server.Respond(HttpStatusCode.OK, """{"domain_name":"pfa.ridelance.ro","incoming":75,"outgoing":20,"storage":0.50}""");

        MailboxUsage usage = await _migadu.GetDomainUsageAsync(default);

        usage.ShouldBe(new MailboxUsage(75, 20, 0.50m));
        _server.Calls.Count.ShouldBe(2);
        _server.Calls[1].Url.ShouldBe("https://api.migadu.com/v1/domains/pfa.ridelance.ro/usage");
    }

    [Fact]
    public async Task DeleteIdentity_SkipsWhatIsAlreadyGone()
    {
        _server.Respond(HttpStatusCode.NotFound, "{}");

        await _migadu.DeleteIdentityAsync("ion.popescu", "rid-ops-ion.popescu", default);

        _server.Calls.ShouldHaveSingleItem().Method.ShouldBe("GET");

        _server.Respond(HttpStatusCode.OK, "{}");
        _server.Respond(HttpStatusCode.OK, "{}");
        await _migadu.DeleteIdentityAsync("ion.popescu", "rid-ops-ion.popescu", default);

        (_server.Calls[2].Method, _server.Calls[2].Url)
            .ShouldBe(("DELETE", "https://api.migadu.com/v1/domains/pfa.ridelance.ro/mailboxes/ion.popescu/identities/rid-ops-ion.popescu"));
    }

    [Fact]
    public async Task WithoutCredentials_NothingIsSent()
    {
        using var http = new HttpClient(_server);
        var unconfigured = new MigaduMailboxProvider(http, Options.Create(new MailboxOptions { Provider = MailboxOptions.MigaduProvider }), NullLogger<MigaduMailboxProvider>.Instance);

        await Should.ThrowAsync<MailboxProviderException>(() => unconfigured.MailboxExistsAsync("ion.popescu", default));
        _server.Calls.ShouldBeEmpty();
    }

    private sealed record Call(string Method, string Url, string? Authorization, string? ContentType, string? Body);

    private sealed class Server : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<Call> Calls { get; } = [];

        public void Respond(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(
                request.Method.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            (HttpStatusCode status, string body) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

using System.Net;
using System.Text;
using Application.Abstractions.Anaf;
using Domain.Accounting;
using Infrastructure.Accounting.Anaf;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

#pragma warning disable CA2000 // Handler-ul și HttpClient-ul trăiesc cât testul.

namespace UnitTests.Accounting;

/// <summary>B4: clientul HTTP pentru <c>ridelance-anaf-validator</c> — contractul, reîncercările, erorile.</summary>
public sealed class AnafValidatorClientTests
{
    private const string ValidResponse = """
        {"valid":true,"declarationType":"D100","validatorVersion":"2026-09","errors":[],"warnings":[{"code":"ATRIBUT","message":"Email invalid ('x@')","field":"email","location":"validari globale"}],
         "rawOutput":"ok","pdfBase64":"JVBERi0xLjQ=","durationMs":1892,"correlationId":"abc"}
        """;

    [Fact]
    public async Task Sends_the_multipart_request_with_the_internal_token()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, ValidResponse));

        Result<AnafValidatorResult> result = await Client(handler).ValidateAsync(DeclarationType.D100, "2026-09", "<x/>"u8.ToArray(), "abc", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (result.Value.Valid, result.Value.RawOutput, result.Value.DurationMs).ShouldBe((true, "ok", 1892L));
        Encoding.ASCII.GetString(result.Value.Pdf!).ShouldBe("%PDF-1.4");
        result.Value.Warnings.ShouldHaveSingleItem().ShouldBe(new AnafValidatorMessage("ATRIBUT", "Email invalid ('x@')", "email", "validari globale"));

        Sent request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.ShouldBe("http://anaf-validator:8080/v1/validate");
        request.Token.ShouldBe("secret");
        request.Body.ShouldContain("name=declarationType");
        request.Body.ShouldContain("D100");
        request.Body.ShouldContain("VALIDATE_AND_PDF");
        request.Body.ShouldContain("<x/>");
    }

    [Fact]
    public async Task Retries_transient_errors_at_most_twice()
    {
        var handler = new ScriptedHandler((HttpStatusCode.ServiceUnavailable, "{}"), (HttpStatusCode.BadGateway, "{}"), (HttpStatusCode.OK, ValidResponse));

        Result<AnafValidatorResult> result = await Client(handler).ValidateAsync(DeclarationType.D100, "2026-09", [1], "abc", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Gives_up_after_the_retries()
    {
        var handler = new ScriptedHandler((HttpStatusCode.GatewayTimeout, "{}"), (HttpStatusCode.GatewayTimeout, "{}"), (HttpStatusCode.GatewayTimeout, "{}"), (HttpStatusCode.OK, ValidResponse));

        Result<AnafValidatorResult> result = await Client(handler).ValidateAsync(DeclarationType.D100, "2026-09", [1], "abc", CancellationToken.None);

        result.Error.Description.ShouldBe("Validatorul ANAF: DUKIntegrator nu a terminat la timp.");
        handler.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Does_not_retry_an_unknown_validator_version()
    {
        var handler = new ScriptedHandler((HttpStatusCode.NotFound, """{"status":404,"error":"Not Found","message":"Versiune necunoscută","correlationId":"abc"}"""));

        Result<AnafValidatorResult> result = await Client(handler).ValidateAsync(DeclarationType.D301, "2030-01", [1], "abc", CancellationToken.None);

        result.Error.Description.ShouldBe("Validatorul ANAF: versiunea de validator 2030-01 nu e instalată în serviciu.");
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Network_errors_are_retried_then_reported()
    {
        var handler = new ScriptedHandler { Throw = true };

        Result<AnafValidatorResult> result = await Client(handler).ValidateAsync(DeclarationType.D100, "2026-09", [1], "abc", CancellationToken.None);

        result.Error.Description.ShouldBe("Validatorul ANAF: serviciul nu răspunde.");
        handler.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task Without_a_url_the_service_is_unavailable()
    {
        var handler = new ScriptedHandler();

        Result<AnafValidatorResult> result = await Client(handler, baseUrl: null).ValidateAsync(DeclarationType.D100, "2026-09", [1], "abc", CancellationToken.None);

        result.Error.Code.ShouldBe("Accounting.AnafValidatorUnavailable");
        handler.Calls.ShouldBe(0);
    }

    private static AnafValidatorClient Client(ScriptedHandler handler, string? baseUrl = "http://anaf-validator:8080") => new(
        new HttpClient(handler),
        Options.Create(new AnafValidatorOptions { BaseUrl = baseUrl, Token = "secret", RetryDelayMilliseconds = 1 }),
        NullLogger<AnafValidatorClient>.Instance);

    private sealed record Sent(string Uri, string? Token, string Body);

    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

        public bool Throw { get; init; }

        public int Calls { get; private set; }

        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw)
            {
                throw new HttpRequestException("Connection refused");
            }

            Requests.Add(new Sent(
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("X-Internal-Token", out IEnumerable<string>? token) ? token.Single() : null,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            (HttpStatusCode status, string body) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

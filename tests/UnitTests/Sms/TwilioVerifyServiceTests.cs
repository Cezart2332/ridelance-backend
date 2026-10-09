using System.Net;
using System.Text;
using Application.Abstractions.Services;
using Infrastructure.Sms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Sms;

/// <summary>Codurile de confirmare prin Twilio Verify: ce trimitem și cum citim răspunsurile.</summary>
public sealed class TwilioVerifyServiceTests
{
    private static readonly TwilioOptions Configured = new()
    {
        AccountSid = "AC123",
        AuthToken = "secret",
        VerifyServiceSid = "VA456",
    };

    [Fact]
    public async Task SendCode_StartsAnSmsVerificationInRomanian()
    {
        using var handler = new StubHandler(HttpStatusCode.Created, """{"status":"pending"}""");
        using var client = new HttpClient(handler, disposeHandler: false);

        Result result = await Service(client).SendCodeAsync("+40712345678");

        result.IsSuccess.ShouldBeTrue();
        handler.Request!.RequestUri!.ToString().ShouldBe("https://verify.twilio.com/v2/Services/VA456/Verifications");
        handler.Request.Headers.Authorization!.Scheme.ShouldBe("Basic");
        Encoding.ASCII.GetString(Convert.FromBase64String(handler.Request.Headers.Authorization.Parameter!)).ShouldBe("AC123:secret");
        handler.Body.ShouldBe("To=%2B40712345678&Channel=sms&Locale=ro");
    }

    [Fact]
    public async Task SendCode_TooManyRequests_SaysSo()
    {
        using var handler = new StubHandler(HttpStatusCode.TooManyRequests, """{"code":60203,"message":"Max send attempts reached"}""");
        using var client = new HttpClient(handler, disposeHandler: false);

        Result result = await Service(client).SendCodeAsync("+40712345678");

        result.Error.Code.ShouldBe("Sms.TooManySends");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"status":"approved"}""", PhoneCodeCheck.Approved)]
    [InlineData(HttpStatusCode.OK, """{"status":"pending"}""", PhoneCodeCheck.Wrong)]
    [InlineData(HttpStatusCode.NotFound, """{"code":20404,"message":"not found"}""", PhoneCodeCheck.Expired)]
    public async Task CheckCode_ReadsTheVerdict(HttpStatusCode status, string body, PhoneCodeCheck expected)
    {
        using var handler = new StubHandler(status, body);
        using var client = new HttpClient(handler, disposeHandler: false);

        Result<PhoneCodeCheck> result = await Service(client).CheckCodeAsync("+40712345678", "123456");

        result.Value.ShouldBe(expected);
        handler.Request!.RequestUri!.ToString().ShouldBe("https://verify.twilio.com/v2/Services/VA456/VerificationCheck");
        handler.Body.ShouldBe("To=%2B40712345678&Code=123456");
    }

    [Fact]
    public async Task WithoutConfiguration_ItIsDisabledAndSendsNothing()
    {
        using var handler = new StubHandler(HttpStatusCode.Created, "{}");
        using var client = new HttpClient(handler, disposeHandler: false);
        TwilioVerifyService service = Service(client, new TwilioOptions());

        service.IsEnabled.ShouldBeFalse();
        (await service.SendCodeAsync("+40712345678")).Error.Code.ShouldBe("Sms.NotConfigured");
        handler.Request.ShouldBeNull();
    }

    private static TwilioVerifyService Service(HttpClient client, TwilioOptions? options = null) =>
        new(client, Options.Create(options ?? Configured), NullLogger<TwilioVerifyService>.Instance);

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}

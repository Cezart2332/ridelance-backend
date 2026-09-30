using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Abstractions.Services;
using Application.FiscalLink;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.FiscalLink;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.FiscalLink;

public sealed class FiscalLinkTests
{
    private const string Tenant = "https://cloud-api.fiscallink.ro/api/tenants/b6ff97d6-318f-417a-a0da-8e29325e174a/integrator/";

    [Fact]
    public async Task CreatingAClientSendsTheManagementKeyAndReadsTheId()
    {
        using var http = new RecordingHandler(HttpStatusCode.Created,
            """{"id":"0f8a1c2e-6b3d-4e7f-9a10-2c4d6e8f0a12","name":"Ion Popescu PFA","cui":"RO12345678","createdAt":"2026-08-27T09:14:22Z"}""");
        using var client = new HttpClient(http);

        Result<Guid> created = await Service(client).CreateClientAsync(
            new FiscalLinkNewClient("Ion Popescu PFA", "RO12345678", "Ion Popescu", "ion@example.test", null, "Strada Test 1, București"));

        created.Value.ShouldBe(Guid.Parse("0f8a1c2e-6b3d-4e7f-9a10-2c4d6e8f0a12"));
        http.Requests.Single().ShouldBe((HttpMethod.Post, new Uri(Tenant + "clients"), "fsk_test_key"));
        using var body = JsonDocument.Parse(http.Bodies.Single()!);
        body.RootElement.GetProperty("name").GetString().ShouldBe("Ion Popescu PFA");
        body.RootElement.GetProperty("cui").GetString().ShouldBe("RO12345678");
        body.RootElement.GetProperty("contactName").GetString().ShouldBe("Ion Popescu");
    }

    [Fact]
    public async Task ActivationAndRegistersAreReadFromTheDocumentedShapes()
    {
        var clientId = Guid.Parse("0f8a1c2e-6b3d-4e7f-9a10-2c4d6e8f0a12");
        using var http = new RecordingHandler(HttpStatusCode.OK,
            """
            {"clientId":"0f8a1c2e-6b3d-4e7f-9a10-2c4d6e8f0a12","clientName":"Acme","activationCode":"3F2A-9C1D-4E5B-8A70-1122-3344-5566-7788",
             "activationLink":"https://app.fiscallink.ro/integrator-activation#code=3F2A-9C1D-4E5B-8A70-1122-3344-5566-7788","registerCount":1,
             "createdAtUtc":"2026-08-27T09:14:22Z","rolledAtUtc":null}
            """,
            """
            {"items":[{"id":"7d2e9b4f-1a3c-4d5e-8f60-b1c2d3e4f5a6","clientId":"0f8a1c2e-6b3d-4e7f-9a10-2c4d6e8f0a12","serialNumber":"DT123456",
              "status":"Active","isOnline":true,"awaitingClientConsent":false,"activatedAtUtc":"2026-08-27T09:14:22Z"}],"page":1,"pageSize":100,"totalCount":1}
            """);
        using var client = new HttpClient(http);
        FiscalLinkService service = Service(client);

        FiscalLinkActivation activation = (await service.GetActivationAsync(clientId)).Value;
        activation.ActivationCode.ShouldBe("3F2A-9C1D-4E5B-8A70-1122-3344-5566-7788");
        activation.ActivationLink.ShouldStartWith("https://app.fiscallink.ro/integrator-activation#code=");

        FiscalLinkRegister register = (await service.ListRegistersAsync(clientId)).Value.Single();
        (register.SerialNumber, register.Status, register.IsOnline).ShouldBe(("DT123456", "Active", true));
        register.ActivatedAtUtc.ShouldBe(new DateTime(2026, 8, 27, 9, 14, 22, DateTimeKind.Utc));

        http.Requests[0].Uri.ShouldBe(new Uri(Tenant + $"clients/{clientId}/activation"));
        http.Requests[1].Uri.ShouldBe(new Uri(Tenant + $"registers?clientId={clientId}&page=1&pageSize=100"));
    }

    /// <summary>Formele văzute pe sandbox: 403 fără corp, 404 cu un string JSON; plus obiectul clasic.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "", "FiscalLink a refuzat cererea (crearea clientului, cod 403).")]
    [InlineData(HttpStatusCode.NotFound, "\"No register with this serial on an active slot of this organization.\"", "FiscalLink: No register with this serial on an active slot of this organization.")]
    [InlineData(HttpStatusCode.BadRequest, """{"title":"Numele este obligatoriu."}""", "FiscalLink: Numele este obligatoriu.")]
    public async Task RejectionsCarryFiscalLinksMessage(HttpStatusCode status, string body, string expected)
    {
        using var http = new RecordingHandler(status, body);
        using var client = new HttpClient(http);

        Result<Guid> result = await Service(client).CreateClientAsync(new FiscalLinkNewClient("X", null, null, null, null, null));

        result.Error.Description.ShouldBe(expected);
    }

    [Fact]
    public async Task ConnectingCreatesTheClientOnceAndNeedsASubscription()
    {
        await using ApplicationDbContext db = Database();
        var user = new User { Id = Guid.NewGuid(), Email = "ion@example.test", FirstName = "Ion", LastName = "Popescu" };
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(), UserId = user.Id, User = user, FullName = "Ion Popescu", Cui = "RO12345678",
            Street = "Strada Test", Number = "1", City = "București",
        };
        db.Users.Add(user);
        db.PfaRegistrations.Add(pfa);
        await db.SaveChangesAsync();

        IFiscalLinkService fiscalLink = DispatchProxy.Create<IFiscalLinkService, FakeFiscalLink>();
        var handler = new ConnectFiscalLinkCommandHandler(db, new CurrentUser(user.Id), fiscalLink);

        (await handler.Handle(new ConnectFiscalLinkCommand(), default)).Error.Code.ShouldBe("FiscalLink.NoSubscription");

        db.UserSubscriptions.Add(new UserSubscription { Id = Guid.NewGuid(), UserId = user.Id, Status = SubscriptionStatus.Active, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        FiscalLinkConnectionDto connected = (await handler.Handle(new ConnectFiscalLinkCommand(), default)).Value;
        connected.Connected.ShouldBeTrue();
        connected.ActivationCode.ShouldBe("3F2A-9C1D");
        (await handler.Handle(new ConnectFiscalLinkCommand(), default)).IsSuccess.ShouldBeTrue();

        var fake = (FakeFiscalLink)(object)fiscalLink;
        fake.Created.Single().ShouldBe(new FiscalLinkNewClient(
            "Ion Popescu PFA", "RO12345678", "Ion Popescu", "ion@example.test", null, "Strada Test 1, București"));
        (await db.FiscalLinkClients.SingleAsync()).FiscalLinkClientId.ShouldBe(FakeFiscalLink.ClientId);
    }

    private static FiscalLinkService Service(HttpClient client) => new(
        client,
        Options.Create(new FiscalLinkOptions { ManagementKey = "fsk_test_key" }),
        NullLogger<FiscalLinkService>.Instance);

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Events());

    private sealed record CurrentUser(Guid UserId) : IUserContext;

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Răspunde pe rând cu corpurile date și ține minte fiecare cerere.</summary>
    private sealed class RecordingHandler(HttpStatusCode status, params string[] responses) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri, string? Key)> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri, request.Headers.TryGetValues("X-Api-Key", out IEnumerable<string>? keys) ? keys.Single() : null));
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            string body = responses[Math.Min(Requests.Count - 1, responses.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    public class FakeFiscalLink : DispatchProxy
    {
        public static readonly Guid ClientId = Guid.Parse("0f8a1c2e-6b3d-4e7f-9a10-2c4d6e8f0a12");

        public List<FiscalLinkNewClient> Created { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IFiscalLinkService.CreateClientAsync):
                    Created.Add((FiscalLinkNewClient)args![0]!);
                    return Task.FromResult(Result.Success(ClientId));
                case nameof(IFiscalLinkService.GetActivationAsync):
                    return Task.FromResult(Result.Success(new FiscalLinkActivation("3F2A-9C1D", "https://app.fiscallink.ro/integrator-activation#code=3F2A-9C1D", 0)));
                default:
                    return Task.FromResult(Result.Success<IReadOnlyList<FiscalLinkRegister>>([]));
            }
        }
    }
}

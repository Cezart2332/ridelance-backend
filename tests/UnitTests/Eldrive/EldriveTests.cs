using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Abstractions.Services;
using Application.Eldrive;
using Domain.Eldrive;
using Domain.Payments;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.Eldrive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Eldrive;

public sealed class EldriveTests
{
    /// <summary>Răspunsul din documentația Eldrive.</summary>
    private const string InviteResponse = """
        {"data":{"id":2642,"partnerId":1452,"sendViaEmail":true,"language":"en","status":"accepted",
        "createdAt":"2026-09-25T08:39:26+00:00","lastUpdatedAt":"2026-09-25T08:39:26+00:00",
        "email":"sofer@example.test","acceptedAt":"2026-09-25T08:39:26+00:00","userId":128832}}
        """;

    [Fact]
    public async Task InviteSendsThePartnerRequestAndReadsTheInviteId()
    {
        using var http = new RecordingHandler(HttpStatusCode.OK, InviteResponse);
        using var client = new HttpClient(http);
        EldriveService service = Service(client);

        Result<EldriveInviteResult> result = await service.InviteAsync("sofer@example.test");

        result.Value.ShouldBe(new EldriveInviteResult(2642, "accepted", 128832));
        http.Method.ShouldBe(HttpMethod.Post);
        http.Uri.ShouldBe(new Uri("https://cp.eldrive.eu/public-api/resources/partner-invites/v2.0"));
        http.Authorization.ShouldBe("Bearer test-key");
        using var body = JsonDocument.Parse(http.Body!);
        body.RootElement.GetProperty("partnerId").GetInt64().ShouldBe(1452);
        body.RootElement.GetProperty("sendViaEmail").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("email").GetString().ShouldBe("sofer@example.test");
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task DeleteTargetsTheSavedInvite(HttpStatusCode status, bool succeeds)
    {
        using var http = new RecordingHandler(status, "");
        using var client = new HttpClient(http);
        Result result = await Service(client).DeleteInviteAsync(2642);

        result.IsSuccess.ShouldBe(succeeds);
        http.Method.ShouldBe(HttpMethod.Delete);
        http.Uri.ShouldBe(new Uri("https://cp.eldrive.eu/public-api/resources/partner-invites/v2.0/2642"));
    }

    [Fact]
    public async Task ConnectingNeedsAnActiveSubscriptionAndHappensOnce()
    {
        await using ApplicationDbContext db = Database();
        User user = await AddUser(db);
        IEldriveService eldrive = DispatchProxy.Create<IEldriveService, FakeEldrive>();
        var handler = new ConnectEldriveCommandHandler(db, new CurrentUser(user.Id), eldrive);

        (await handler.Handle(new ConnectEldriveCommand("sofer@example.test"), default)).Error.Code.ShouldBe("Eldrive.NoSubscription");

        db.UserSubscriptions.Add(new UserSubscription { Id = Guid.NewGuid(), UserId = user.Id, Status = SubscriptionStatus.Active, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        (await handler.Handle(new ConnectEldriveCommand("nu-e-email"), default)).Error.Code.ShouldBe("Eldrive.InvalidEmail");

        Result<EldriveConnectionDto> connected = await handler.Handle(new ConnectEldriveCommand(" sofer@example.test "), default);
        connected.Value.Connected.ShouldBeTrue();
        EldriveInvite invite = await db.EldriveInvites.SingleAsync();
        (invite.EldriveInviteId, invite.Email, invite.Status).ShouldBe((2642L, "sofer@example.test", "accepted"));

        (await handler.Handle(new ConnectEldriveCommand("sofer@example.test"), default)).Error.Code.ShouldBe("Eldrive.AlreadyConnected");
    }

    [Fact]
    public async Task AdminSeesCancelledSubscribersFirstAndRemovesTheirInvite()
    {
        await using ApplicationDbContext db = Database();
        User cancelled = await AddUser(db, "anulat@example.test");
        User active = await AddUser(db, "activ@example.test");
        db.UserSubscriptions.AddRange(
            new UserSubscription { Id = Guid.NewGuid(), UserId = cancelled.Id, Status = SubscriptionStatus.Cancelled, CreatedAtUtc = DateTime.UtcNow },
            new UserSubscription { Id = Guid.NewGuid(), UserId = active.Id, Status = SubscriptionStatus.Active, CreatedAtUtc = DateTime.UtcNow });
        db.EldriveInvites.AddRange(
            new EldriveInvite { Id = Guid.NewGuid(), UserId = active.Id, Email = active.Email, EldriveInviteId = 1, Status = "accepted", CreatedAtUtc = DateTime.UtcNow },
            new EldriveInvite { Id = Guid.NewGuid(), UserId = cancelled.Id, Email = cancelled.Email, EldriveInviteId = 2, Status = "accepted", CreatedAtUtc = DateTime.UtcNow.AddDays(-5) });
        await db.SaveChangesAsync();

        List<EldriveInviteAdminDto> list = (await new GetEldriveInvitesQueryHandler(db).Handle(new GetEldriveInvitesQuery(), default)).Value;
        list.Select(i => (i.AccountEmail, i.SubscriptionActive)).ShouldBe([("anulat@example.test", false), ("activ@example.test", true)]);

        IEldriveService eldrive = DispatchProxy.Create<IEldriveService, FakeEldrive>();
        var adminId = Guid.NewGuid();
        (await new RemoveEldriveInviteCommandHandler(db, new CurrentUser(adminId), eldrive)
            .Handle(new RemoveEldriveInviteCommand(list[0].Id), default)).IsSuccess.ShouldBeTrue();

        ((FakeEldrive)(object)eldrive).Deleted.ShouldBe([2L]);
        EldriveInvite removed = await db.EldriveInvites.SingleAsync(i => i.UserId == cancelled.Id);
        removed.RemovedAtUtc.ShouldNotBeNull();
        removed.RemovedByUserId.ShouldBe(adminId);
    }

    private static EldriveService Service(HttpClient client) => new(
        client,
        Options.Create(new EldriveOptions { ApiKey = "test-key" }),
        NullLogger<EldriveService>.Instance);

    private static async Task<User> AddUser(ApplicationDbContext db, string email = "sofer@example.test")
    {
        var user = new User { Id = Guid.NewGuid(), Email = email, FirstName = "Test", LastName = "Sofer" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static ApplicationDbContext Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Events());

    private sealed record CurrentUser(Guid UserId) : IUserContext;

    private sealed class Events : Infrastructure.DomainEvents.IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    public class FakeEldrive : DispatchProxy
    {
        public List<long> Deleted { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IEldriveService.InviteAsync))
            {
                return Task.FromResult(Result.Success(new EldriveInviteResult(2642, "accepted", 128832)));
            }

            Deleted.Add((long)args![0]!);
            return Task.FromResult(Result.Success());
        }
    }
}

using System.Net;
using System.Text;
using RidelanceSpv.Core.Anaf;
using RidelanceSpv.Core.Server;
using RidelanceSpv.Core.Sync;
using Shouldly;

namespace RidelanceSpv.Tests;

public sealed class SyncEngineTests
{
    private static readonly Guid Run = Guid.NewGuid();

    [Fact]
    public async Task Only_new_messages_of_our_clients_are_downloaded_and_sent()
    {
        var spv = new FakeSpv(
            Message("1", "12345674"),
            Message("2", "RO12345674"),
            Message("3", "99999990"));
        var server = new FakeServer(new SpvRunStart(Run, 5, ["12345674"], [])) { Known = ["1"] };

        SyncReport report = await new SyncEngine(spv, server, "PC", "1.0").RunAsync(null, CancellationToken.None);

        report.Succeeded.ShouldBeTrue();
        report.Listed.ShouldBe(2);
        report.Sent.ShouldBe(1);
        spv.Days.ShouldBe(5);
        spv.Downloads.ShouldBe(["2"]);
        server.AskedIds.ShouldBe(["1", "2"]);
        server.Sent.ShouldBe(["2"]);
        server.Finished.ShouldBe([(string?)null]);
    }

    [Fact]
    public async Task A_failed_download_stays_for_next_time_and_requests_still_go_out()
    {
        var spv = new FakeSpv(Message("1", "12345674")) { FailDownload = "1", RequestIds = { ["VECTOR FISCAL"] = "777" } };
        var server = new FakeServer(new SpvRunStart(Run, 60, ["12345674"], [new SpvRequestToSend(Guid.NewGuid(), "VECTOR FISCAL", "12345674", [])]));

        SyncReport report = await new SyncEngine(spv, server, "PC", "1.0").RunAsync(null, CancellationToken.None);

        report.Succeeded.ShouldBeTrue();
        report.Failed.ShouldBe(1);
        server.Sent.ShouldBeEmpty();
        server.Reported.ShouldHaveSingleItem().AnafRequestId.ShouldBe("777");
    }

    [Fact]
    public async Task A_spv_error_closes_the_run_with_the_message()
    {
        var spv = new FakeSpv { FailList = "SPV a refuzat certificatul." };
        var server = new FakeServer(new SpvRunStart(Run, 60, ["12345674"], []));

        SyncReport report = await new SyncEngine(spv, server, "PC", "1.0").RunAsync(null, CancellationToken.None);

        report.Error.ShouldBe("SPV a refuzat certificatul.");
        server.Finished.ShouldBe(["SPV a refuzat certificatul."]);
    }

    [Fact]
    public async Task Another_run_in_progress_is_skipped_quietly()
    {
        var server = new FakeServer(null);

        SyncReport report = await new SyncEngine(new FakeSpv(), server, "PC", "1.0").RunAsync(null, CancellationToken.None);

        report.Skipped.ShouldBeTrue();
        report.Error.ShouldBeNull();
    }

    [Fact]
    public async Task The_spv_list_is_read_with_the_romanian_time_and_no_messages_is_empty()
    {
        using var handler = new Scripted(
            """{"mesaje":[{"data_creare":"15.09.2026 14:30:00","cif":"12345674","id_solicitare":"55","detalii":"recipisa pentru CIF 12345674, tip D100","tip":"RECIPISA","id":"900"}],"serial":"x","titlu":"Lista Mesaje"}""",
            """{"eroare":"Nu exista mesaje in ultimele 5 zile","titlu":"Lista Mesaje"}""");
        using var client = new SpvClient(new HttpClient(handler), "https://spv.test/rest");

        SpvListedMessage message = (await client.ListMessagesAsync(5, CancellationToken.None)).ShouldHaveSingleItem();
        message.Type.ShouldBe("RECIPISA");
        message.RequestId.ShouldBe("55");
        message.CreatedAtUtc.ShouldBe(new DateTime(2026, 9, 15, 11, 30, 0, DateTimeKind.Utc));
        handler.Urls[0].ShouldBe("https://spv.test/rest/listaMesaje?zile=5");

        (await client.ListMessagesAsync(5, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_request_returns_the_anaf_id_or_the_anaf_error()
    {
        using var handler = new Scripted("""{"id_solicitare":"4242"}""", """{"eroare":"Tip necunoscut"}""");
        using var client = new SpvClient(new HttpClient(handler), "https://spv.test/rest");

        (await client.RequestAsync("Fisa Rol", "12345674", new Dictionary<string, string> { ["an"] = "2026" }, CancellationToken.None)).ShouldBe("4242");
        handler.Urls[0].ShouldBe("https://spv.test/rest/cerere?tip=Fisa%20Rol&cui=12345674&an=2026");
        (await Should.ThrowAsync<SpvException>(() => client.RequestAsync("X", "1", new Dictionary<string, string>(), CancellationToken.None))).Message.ShouldBe("ANAF: Tip necunoscut");
    }

    private static SpvListedMessage Message(string id, string cif) => new(id, cif, "NOTIFICARE", DateTime.UtcNow, null, null);

    private sealed class FakeSpv(params SpvListedMessage[] messages) : ISpvApi
    {
        public int Days { get; private set; }
        public List<string> Downloads { get; } = [];
        public string? FailDownload { get; init; }
        public string? FailList { get; init; }
        public Dictionary<string, string> RequestIds { get; } = [];

        public Task<IReadOnlyList<SpvListedMessage>> ListMessagesAsync(int days, CancellationToken cancellationToken)
        {
            Days = days;
            return FailList is null ? Task.FromResult<IReadOnlyList<SpvListedMessage>>(messages) : throw new SpvException(FailList);
        }

        public Task<SpvDocument> DownloadAsync(string messageId, CancellationToken cancellationToken)
        {
            if (messageId == FailDownload)
            {
                throw new SpvException("indisponibil");
            }

            Downloads.Add(messageId);
            return Task.FromResult(new SpvDocument($"{messageId}.pdf", "application/pdf", "%PDF"u8.ToArray()));
        }

        public Task<string> RequestAsync(string type, string cui, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) =>
            Task.FromResult(RequestIds[type]);
    }

    private sealed class FakeServer(SpvRunStart? start) : IRidelanceApi
    {
        public List<string> Known { get; init; } = [];
        public List<string> AskedIds { get; } = [];
        public List<string> Sent { get; } = [];
        public List<(Guid RequestId, string? AnafRequestId, string? Error)> Reported { get; } = [];
        public List<string?> Finished { get; } = [];

        public Task<SpvAgentStatus> StatusAsync(CancellationToken cancellationToken) => Task.FromResult(new SpvAgentStatus("PC", 1, null, 0));

        public Task<SpvRunStart> StartRunAsync(string machine, string agentVersion, CancellationToken cancellationToken) =>
            start is null ? throw new RidelanceException("Spv.RunInProgress", "în curs") : Task.FromResult(start);

        public Task<IReadOnlyList<string>> NewIdsAsync(Guid runId, IReadOnlyList<string> ids, CancellationToken cancellationToken)
        {
            AskedIds.AddRange(ids);
            return Task.FromResult<IReadOnlyList<string>>([.. ids.Where(id => !Known.Contains(id))]);
        }

        public Task SendMessageAsync(Guid runId, SpvListedMessage message, SpvDocument? document, CancellationToken cancellationToken)
        {
            Sent.Add(message.Id);
            return Task.CompletedTask;
        }

        public Task ReportRequestAsync(Guid runId, Guid requestId, string? anafRequestId, string? error, CancellationToken cancellationToken)
        {
            Reported.Add((requestId, anafRequestId, error));
            return Task.CompletedTask;
        }

        public Task FinishRunAsync(Guid runId, string? error, CancellationToken cancellationToken)
        {
            Finished.Add(error);
            return Task.CompletedTask;
        }
    }

    private sealed class Scripted(params string[] bodies) : HttpMessageHandler
    {
        private readonly Queue<string> _bodies = new(bodies);

        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_bodies.Dequeue(), Encoding.UTF8, "application/json") });
        }
    }
}

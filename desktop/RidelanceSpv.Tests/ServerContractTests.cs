using RidelanceSpv.Core.Anaf;
using RidelanceSpv.Core.Server;
using RidelanceSpv.Core.Sync;
using Shouldly;

namespace RidelanceSpv.Tests;

/// <summary>
/// Contractul cu serverul RIDElance, pe HTTP real (JSON, multipart, cheia din antet), cu ANAF
/// simulat. Rulează doar cu <c>RIDELANCE_SPV_SERVER</c> și <c>RIDELANCE_SPV_KEY</c> setate (un
/// backend de test cu o cheie SPV și un PFA cu CUI 12345674); altfel nu face nimic.
/// </summary>
public sealed class ServerContractTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("RIDELANCE_SPV_SERVER");
    private static readonly string? Key = Environment.GetEnvironmentVariable("RIDELANCE_SPV_KEY");

    [Fact]
    public async Task A_run_sends_new_messages_once_and_the_next_run_sends_nothing()
    {
        if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(Key))
        {
            return;
        }

        string id = $"it-{Guid.NewGuid():N}"[..20];
        var spv = new OneMessage(new SpvListedMessage(id, "12345674", "NOTIFICARE", DateTime.UtcNow.AddHours(-1), null, "Notificare de test"));
        using var server = new RidelanceClient(Server, Key);

        SpvAgentStatus status = await server.StatusAsync(CancellationToken.None);
        status.Pfas.ShouldBeGreaterThan(0);

        SyncReport first = await new SyncEngine(spv, server, "TEST", "1.0.0").RunAsync(null, CancellationToken.None);
        first.Error.ShouldBeNull();
        first.Sent.ShouldBe(1);

        SyncReport second = await new SyncEngine(spv, server, "TEST", "1.0.0").RunAsync(null, CancellationToken.None);
        second.Error.ShouldBeNull();
        second.Sent.ShouldBe(0);
        second.Days.ShouldBeLessThanOrEqualTo(3);

        (await server.StatusAsync(CancellationToken.None)).LastSuccessAtUtc.ShouldNotBeNull();
    }

    private sealed class OneMessage(SpvListedMessage message) : ISpvApi
    {
        public Task<IReadOnlyList<SpvListedMessage>> ListMessagesAsync(int days, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpvListedMessage>>([message]);

        public Task<SpvDocument> DownloadAsync(string messageId, CancellationToken cancellationToken) =>
            Task.FromResult(new SpvDocument($"{messageId}.pdf", "application/pdf", "%PDF-1.4 notificare"u8.ToArray()));

        public Task<string> RequestAsync(string type, string cui, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) =>
            Task.FromResult("1");
    }
}

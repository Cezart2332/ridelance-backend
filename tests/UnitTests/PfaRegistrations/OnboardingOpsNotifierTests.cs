using Application.Abstractions;
using Application.PfaRegistrations.Onboarding.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>Cine primește ce: dosarul de înființare PFA pleacă la Consulto, restul rămâne la noi.</summary>
public sealed class OnboardingOpsNotifierTests
{
    private static readonly EmailAttachmentContent Archive = new("dosar.zip", "application/zip", [1, 2, 3]);

    [Fact]
    public async Task OnlyThePfaDossier_GoesToConsulto()
    {
        var email = new Outbox();
        OnboardingOpsNotifier notifier = Notifier(email, ops: "contact@ridelance.ro", consulto: "office@consulto.ro");

        await notifier.PfaDossierReadyAsync("Popescu Ion", "ion@example.ro", null, 24900, Archive, CancellationToken.None);
        await notifier.PaymentReceivedAsync("Abonament PFA Full", 29900, "ion@example.ro", "pi_1", CancellationToken.None);
        await notifier.ServiceDossierReadyAsync("Start Ride", "Popescu Ion", "ion@example.ro", "0722", 99900, Archive, CancellationToken.None);

        email.Sent.ShouldBe(
        [
            ("office@consulto.ro", "Dosar PFA - Popescu Ion"),
            ("contact@ridelance.ro", "Plată încasată: Abonament PFA Full"),
            ("contact@ridelance.ro", "Start Ride - Popescu Ion"),
        ]);
    }

    /// <summary>Un mediu fără adresa Consulto (site-ul de test) nu trimite dosarul în afară.</summary>
    [Fact]
    public async Task WithoutAConsultoAddress_TheDossierStaysWithUs()
    {
        var email = new Outbox();

        await Notifier(email, ops: "test@ridelance.ro", consulto: null)
            .PfaDossierReadyAsync("Popescu Ion", "ion@example.ro", null, 24900, Archive, CancellationToken.None);
        await Notifier(email, ops: null, consulto: " ")
            .PfaDossierReadyAsync("Popescu Ion", "ion@example.ro", null, 24900, Archive, CancellationToken.None);

        email.Sent.Select(sent => sent.To).ShouldBe(["test@ridelance.ro", "contact@ridelance.ro"]);
    }

    private static OnboardingOpsNotifier Notifier(Outbox email, string? ops, string? consulto) => new(
        email,
        new Passthrough(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:OpsEmail"] = ops,
            ["Notifications:ConsultoEmail"] = consulto?.Trim(),
        }).Build(),
        NullLogger<OnboardingOpsNotifier>.Instance);

    private sealed class Passthrough : IMjmlRenderer
    {
        public string Render(string mjml) => mjml;
    }

    private sealed class Outbox : IEmailService
    {
        public List<(string To, string Subject)> Sent { get; } = [];

        public Task<Result> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            Sent.Add((to, subject));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> SendEmailWithAttachmentsAsync(
            string to, string subject, string htmlBody, IReadOnlyList<EmailAttachmentContent> attachments,
            bool highPriority = false, CancellationToken cancellationToken = default)
        {
            Sent.Add((to, subject));
            return Task.FromResult(Result.Success());
        }
    }
}

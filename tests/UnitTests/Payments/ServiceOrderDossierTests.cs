using System.IO.Compression;
using Application.Abstractions;
using Application.Abstractions.Dossiers;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Application.Payments.ServiceOrders;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using Application.PfaRegistrations.Onboarding.Notifications;
using Domain.Payments;
using Domain.PfaRegistrations.CompanyFormation;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Payments;

/// <summary>
/// Serviciile de pe site cer aceleași date ca „Nu am PFA" din onboarding, fără cont. Formularul
/// incomplet nu ajunge la plată, iar după plată dosarul pleacă o singură dată spre Consulto.
/// </summary>
public sealed class ServiceOrderDossierTests
{
    private const string ValidCnp = "5010519420017";
    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    private const string Pdf = "data:application/pdf;base64,JVBERi0xLjQKJcOkw7zDtsOfCg==";

    [Fact]
    public async Task Infiintarea_completa_devine_dosar_semnat_cu_CNP_criptat()
    {
        using ApplicationDbContext db = await SeededDbAsync();
        var files = new MemoryFiles();

        Result<ServiceOrderDossier> result = await Builder(db, files).BuildAsync(
            ServiceDossierKinds.InfiintarePfa, FormationPayload(), Context(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : string.Empty);
        ServiceOrderDossier dossier = result.Value;
        dossier.Solicitant.CnpEncrypted.ShouldBe($"enc:{ValidCnp}");
        dossier.Solicitant.CnpMasked.ShouldNotBe(ValidCnp);
        dossier.Consents.Select(c => c.StepKey).ShouldBe(["mandat", "gdpr"]);
        dossier.Signature.ShouldNotBeNull();
        dossier.Signature.PayloadHash.ShouldNotBeNullOrWhiteSpace();
        dossier.Signature.IpAddress.ShouldBe("81.0.0.1");
        dossier.IdentityDocument!.ContentType.ShouldBe("application/pdf");
        dossier.IncludesVatIntracom.ShouldBeFalse();
        files.Count.ShouldBe(2);

        // Ce se salvează pe comandă se citește înapoi la fel.
        var parsed = ServiceOrderDossier.Parse(dossier.Serialize());
        parsed!.Solicitant.CnpEncrypted.ShouldBe($"enc:{ValidCnp}");
        parsed.ConsultoOfficeId.ShouldBe(OfficeId);
    }

    [Fact]
    public async Task Start_Ride_are_TVA_intracomunitar_inclus()
    {
        using ApplicationDbContext db = await SeededDbAsync();

        Result<ServiceOrderDossier> result = await Builder(db, new MemoryFiles()).BuildAsync(
            ServiceDossierKinds.StartRide, FormationPayload(), Context(), CancellationToken.None);

        result.Value.IncludesVatIntracom.ShouldBeTrue();
    }

    [Fact]
    public async Task Fara_semnatura_infiintarea_nu_ajunge_la_plata()
    {
        using ApplicationDbContext db = await SeededDbAsync();

        Result<ServiceOrderDossier> result = await Builder(db, new MemoryFiles()).BuildAsync(
            ServiceDossierKinds.InfiintarePfa, FormationPayload() with { Signature = null }, Context(), CancellationToken.None);

        result.Error.ShouldBe(CompanyFormationErrors.SignatureMissing);
    }

    [Fact]
    public async Task Un_acord_lipsa_opreste_comanda()
    {
        using ApplicationDbContext db = await SeededDbAsync();
        ServiceDossierPayload payload = FormationPayload();
        payload = payload with { Signature = payload.Signature! with { Consents = [new ConsentPayload("mandat")] } };

        Result<ServiceOrderDossier> result = await Builder(db, new MemoryFiles()).BuildAsync(
            ServiceDossierKinds.InfiintarePfa, payload, Context(), CancellationToken.None);

        result.Error.ShouldBe(CompanyFormationErrors.ConsentIncomplete);
    }

    [Fact]
    public async Task CNP_invalid_si_act_lipsa_sunt_refuzate()
    {
        using ApplicationDbContext db = await SeededDbAsync();
        ServiceDossierPayload payload = FormationPayload();

        (await Builder(db, new MemoryFiles()).BuildAsync(
            ServiceDossierKinds.InfiintarePfa,
            payload with { Solicitant = payload.Solicitant! with { Cnp = "5010519420010" } },
            Context(),
            CancellationToken.None)).Error.ShouldBe(CompanyFormationErrors.InvalidCnp);

        (await Builder(db, new MemoryFiles()).BuildAsync(
            ServiceDossierKinds.InfiintarePfa,
            payload with { IdentityDocument = null },
            Context(),
            CancellationToken.None)).Error.ShouldBe(ServiceOrderErrors.IdentityDocumentMissing);
    }

    [Fact]
    public async Task Adresa_proprie_fara_cod_postal_nu_e_sediu_complet()
    {
        using ApplicationDbContext db = await SeededDbAsync();
        ServiceDossierPayload payload = FormationPayload() with
        {
            Office = new RegisteredOfficePayload("Own", null, true, Address() with { CodPostal = null }, true, true, null, []),
        };

        Result<ServiceOrderDossier> result = await Builder(db, new MemoryFiles()).BuildAsync(
            ServiceDossierKinds.InfiintarePfa, payload, Context(), CancellationToken.None);

        result.Error.ShouldBe(CompanyFormationErrors.RegisteredOfficeIncomplete);
    }

    [Fact]
    public async Task Gazduirea_cere_zona_dar_nu_semnatura()
    {
        using ApplicationDbContext db = await SeededDbAsync();
        ServiceDossierPayload hosting = FormationPayload() with
        {
            Signature = null,
            CompanyName = "Popescu Ion PFA",
            CompanyCui = "RO123",
        };

        Result<ServiceOrderDossier> ok = await Builder(db, new MemoryFiles()).BuildAsync(
            ServiceDossierKinds.SediuSocial, hosting, Context(), CancellationToken.None);

        ok.IsSuccess.ShouldBeTrue(ok.IsFailure ? ok.Error.Description : string.Empty);
        ok.Value.Signature.ShouldBeNull();
        ok.Value.CompanyName.ShouldBe("Popescu Ion PFA");
        ok.Value.OfficeType.ShouldBe(RegisteredOfficeType.ConsultoProvided);

        Result<ServiceOrderDossier> noOffice = await Builder(db, new MemoryFiles()).BuildAsync(
            ServiceDossierKinds.SediuSocial, hosting with { Office = null }, Context(), CancellationToken.None);

        noOffice.Error.ShouldBe(ServiceOrderErrors.OfficeRequired);
    }

    [Fact]
    public async Task Dupa_plata_arhiva_pleaca_o_singura_data()
    {
        using ApplicationDbContext db = await SeededDbAsync();
        var files = new MemoryFiles();
        ServiceOrderDossier dossier = (await Builder(db, files).BuildAsync(
            ServiceDossierKinds.StartRide, FormationPayload(), Context(), CancellationToken.None)).Value;

        var order = new ServiceOrder
        {
            Id = Guid.NewGuid(),
            ServiceKey = ServiceDossierKinds.StartRide,
            ServiceTitle = "Start Ride",
            CustomerName = "Ion Popescu",
            CustomerEmail = "ion@example.ro",
            CustomerPhone = "0722000000",
            Status = ServiceOrderStatus.Paid,
            AmountBani = Pricing.Services.StartRideBani,
            PaidAtUtc = DateTime.UtcNow,
            DossierJson = dossier.Serialize(),
        };
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var email = new CapturingEmail();
        ServiceOrderDossierSender sender = Sender(db, files, email);

        await sender.SendIfReadyAsync(order.Id, CancellationToken.None);
        await sender.SendIfReadyAsync(order.Id, CancellationToken.None);

        email.Sent.Count.ShouldBe(1);
        EmailAttachmentContent archive = email.Sent[0].ShouldHaveSingleItem();
        archive.FileName.ShouldStartWith("start-ride-");

        using var zip = new ZipArchive(new MemoryStream(archive.Content));
        string[] names = [.. zip.Entries.Select(e => Path.GetFileName(e.FullName))];
        names.ShouldBe(["date-solicitant.pdf", "dovada-consimtamant.pdf", "semnatura.png", "act-identitate.pdf", "metadata.json"], ignoreOrder: true);

        (await db.ServiceOrders.SingleAsync()).SentToConsultoAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Neplatita_nu_pleaca()
    {
        using ApplicationDbContext db = await SeededDbAsync();
        var files = new MemoryFiles();
        ServiceOrderDossier dossier = (await Builder(db, files).BuildAsync(
            ServiceDossierKinds.InfiintarePfa, FormationPayload(), Context(), CancellationToken.None)).Value;

        var order = new ServiceOrder
        {
            Id = Guid.NewGuid(),
            ServiceKey = ServiceDossierKinds.InfiintarePfa,
            ServiceTitle = "Înființare PFA",
            Status = ServiceOrderStatus.Pending,
            DossierJson = dossier.Serialize(),
        };
        db.ServiceOrders.Add(order);
        await db.SaveChangesAsync();

        var email = new CapturingEmail();
        await Sender(db, files, email).SendIfReadyAsync(order.Id, CancellationToken.None);

        email.Sent.ShouldBeEmpty();
    }

    // ─── Date de test ───

    private static readonly Guid OfficeId = Guid.NewGuid();

    private static AdresaPayload Address() =>
        new("Ilfov", "Voluntari", "Pipera", "12", null, null, null, "4", "077190");

    private static ServiceDossierPayload FormationPayload() => new(
        new PersoanaFizicaPayload(
            "Popescu",
            "Ion",
            ValidCnp,
            "CI",
            "IF",
            "123456",
            "SPCLEP Voluntari",
            new DateOnly(2020, 1, 10),
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(5),
            Address()),
        new RegisteredOfficePayload("ConsultoProvided", OfficeId, null, null, false, false, null, []),
        null,
        null,
        new ServiceFilePayload("buletin.pdf", "application/pdf", Pdf),
        new SignaturePayload(Png, "[]", 400, 200, [new ConsentPayload("mandat"), new ConsentPayload("gdpr")]));

    private static SignatureContext Context() => new("81.0.0.1", "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0) Safari/604.1", null);

    private static async Task<ApplicationDbContext> SeededDbAsync()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new Events());

        db.ConsultoOffices.Add(new ConsultoOffice
        {
            Id = OfficeId,
            Adresa = new Adresa { Judet = "București", Localitate = "Sectorul 1", Strada = "Victoriei", Numar = "1" },
            IsActive = true,
        });

        var flowId = Guid.NewGuid();
        db.LegalConsentFlows.Add(new LegalConsentFlow
        {
            Id = flowId,
            Context = "infiintare-societate",
            Version = "1.0",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            Steps =
            [
                new LegalConsentStep { Id = Guid.NewGuid(), LegalConsentFlowId = flowId, Position = 1, Key = "mandat", Title = "Mandat", Body = "Text mandat", CheckboxLabel = "Accept" },
                new LegalConsentStep { Id = Guid.NewGuid(), LegalConsentFlowId = flowId, Position = 2, Key = "gdpr", Title = "GDPR", Body = "Text GDPR", CheckboxLabel = "Accept" },
            ],
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static ServiceOrderDossierBuilder Builder(ApplicationDbContext db, MemoryFiles files) =>
        new(db, new PrefixProtector(), files);

    private static ServiceOrderDossierSender Sender(ApplicationDbContext db, MemoryFiles files, CapturingEmail email)
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        return new ServiceOrderDossierSender(
            db,
            files,
            new FakePdf(),
            new OnboardingOpsNotifier(email, new IdentityRenderer(), config, NullLogger<OnboardingOpsNotifier>.Instance),
            NullLogger<ServiceOrderDossierSender>.Instance);
    }

    private sealed class PrefixProtector : ISecretProtector
    {
        public string Protect(string plainText) => $"enc:{plainText}";

        public string Unprotect(string protectedText) => protectedText["enc:".Length..];
    }

    private sealed class MemoryFiles : IFileEncryptionService
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public int Count => _files.Count;

        public async Task<EncryptedFileResult> EncryptAndSaveAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            await fileStream.CopyToAsync(memory, cancellationToken);
            _files[fileName] = memory.ToArray();
            return new EncryptedFileResult(fileName, "iv");
        }

        public Task<Stream> DecryptAndReadAsync(string encryptedFilePath, string iv, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(_files[encryptedFilePath]));
    }

    private sealed class FakePdf : ICompanyFormationPdfGenerator
    {
        public byte[] GenerateApplicantSheet(CompanyFormationSheetData data) => [1];

        public byte[] GenerateConsentProof(CompanyFormationConsentProofData data) => [2];
    }

    private sealed class CapturingEmail : IEmailService
    {
        public List<IReadOnlyList<EmailAttachmentContent>> Sent { get; } = [];

        public Task<Result> SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> SendEmailWithAttachmentsAsync(
            string to,
            string subject,
            string htmlBody,
            IReadOnlyList<EmailAttachmentContent> attachments,
            bool highPriority = false,
            CancellationToken cancellationToken = default)
        {
            Sent.Add(attachments);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class IdentityRenderer : IMjmlRenderer
    {
        public string Render(string mjml) => mjml;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

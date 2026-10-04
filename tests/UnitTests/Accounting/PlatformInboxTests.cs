using System.Text;
using Application.Abstractions.Ai;
using Application.Abstractions.Authentication;
using Application.Abstractions.Services;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Încărcarea globală din „Clienți PFA”: alocarea după CUI-ul din text, după numele fișierului, după
/// comision (raportul Bolt fără CUI ↔ factura de comision Bolt), CUI-ul necunoscut raportat, „De verificat”.
/// </summary>
public sealed class PlatformInboxTests : IDisposable
{
    private const string Period = "2026-08";

    // CUI-uri cu cifra de control corectă.
    private const string IonCui = "41234564";
    private const string MariaCui = "38123450";
    private const string StrangerCui = "44000118";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _ion = Guid.NewGuid();
    private readonly Guid _maria = Guid.NewGuid();
    private readonly FakeExtractor _extractor = new();
    private readonly MemoryEncryption _files = new();
    private readonly IOptions<AccountingOptions> _options = Options.Create(new AccountingOptions());

    public PlatformInboxTests()
    {
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance", Role = UserRole.Admin });
        AddPfa(_ion, IonCui, "Ion", "Popescu");
        AddPfa(_maria, MariaCui, "Maria", "Ionescu");
        _db.SaveChanges();
    }

    [Fact]
    public void Matcher_finds_valid_cuis_and_ignores_amounts_and_bad_checksums()
    {
        PlatformInboxMatcher.ValidCuisIn($"CIF: RO{IonCui}\nTotal 41.234.564,00\nAlt număr 41234565").ShouldBe([IonCui]);
        PlatformInboxMatcher.LabelledCuisIn($"Cod fiscal: {StrangerCui}\n{MariaCui}").ShouldBe([StrangerCui]);
        PlatformInboxMatcher.NormalizeCui("RO 0041234564").ShouldBe(IonCui);
        PlatformInboxMatcher.PeriodFromFileName("bolt_raport_08.2026.pdf").ShouldBe("2026-08");
        PlatformInboxMatcher.PeriodFromFileName("factura-2026_07.pdf").ShouldBe("2026-07");
        PlatformInboxMatcher.PeriodFromFileName("raport.pdf").ShouldBeNull();
    }

    [Fact]
    public void Matcher_uses_the_name_in_the_file_name_only_when_it_is_unique()
    {
        var clients = new List<InboxClient>
        {
            new(_ion, IonCui, ["Popescu Ion PFA"]),
            new(_maria, MariaCui, ["Ionescu Maria PFA"]),
            new(Guid.NewGuid(), null, ["Maria Ionescu II"]),
        };

        PlatformInboxMatcher.ByFileName("Raport Bolt - ION POPESCU - august.pdf", clients)!.PfaId.ShouldBe(_ion);
        PlatformInboxMatcher.ByFileName($"factura_{MariaCui}.pdf", clients)!.PfaId.ShouldBe(_maria);
        PlatformInboxMatcher.ByFileName("ionescu_maria.pdf", clients).ShouldBeNull();
        PlatformInboxMatcher.ByFileName("raport_bolt.pdf", clients).ShouldBeNull();
    }

    [Fact]
    public async Task Cui_in_the_document_assigns_it_to_that_client()
    {
        PlatformInboxResultDto result = (await Upload(("factura.pdf", $"Bolt Operations OÜ\nClient: Ion Popescu PFA\nCIF: RO{IonCui}\nComision: 1.000,00 RON"))).Single();

        result.Status.ShouldBe(PlatformInboxStatus.Assigned);
        result.PfaId.ShouldBe(_ion);
        result.MatchedBy.ShouldBe(PlatformInboxMatch.Cui);
        PlatformDocument document = await _db.PlatformDocuments.SingleAsync();
        document.PfaRegistrationId.ShouldBe(_ion);
        document.Status.ShouldBe(PlatformDocumentStatus.Extracting);
        document.Period.ShouldBe(Period);
        (await Inbox()).ShouldBeEmpty();
    }

    [Fact]
    public async Task File_name_assigns_it_when_the_document_has_no_cui_and_sets_the_month()
    {
        PlatformInboxResultDto result = (await Upload(("Raport Bolt Maria Ionescu 07.2026.pdf", "Raport săptămânal\nVenit brut 5.000,00"))).Single();

        result.Status.ShouldBe(PlatformInboxStatus.Assigned);
        result.PfaId.ShouldBe(_maria);
        result.MatchedBy.ShouldBe(PlatformInboxMatch.FileName);
        (await _db.PlatformDocuments.SingleAsync()).Period.ShouldBe("2026-07");
    }

    [Fact]
    public async Task Unknown_cui_is_reported_in_admin()
    {
        PlatformInboxResultDto result = (await Upload(("factura.pdf", $"Bolt Operations OÜ\nClient: Altcineva PFA\nCUI: {StrangerCui}"))).Single();

        result.Status.ShouldBe(PlatformInboxStatus.UnknownCui);
        PlatformInboxItemDto item = (await Inbox()).Single();
        item.DetectedCui.ShouldBe(StrangerCui);
        item.Reason.ShouldBe($"CUI {StrangerCui} nu aparține niciunui client PFA.");
        (await _db.PlatformDocuments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Bolt_report_without_cui_is_assigned_by_the_commission_of_the_bolt_invoice()
    {
        await ClientInvoice(_ion, 1000m);
        await ClientInvoice(_maria, 750m);

        PlatformInboxResultDto uploaded = (await Upload(("raport.pdf", "Raport Bolt\nVenit brut 5.000,00\nComision 750,00"))).Single();
        uploaded.Status.ShouldBe(PlatformInboxStatus.Matching);

        _extractor.Next = Reading(PlatformDocumentType.PlatformReport, 750m);
        await Match(uploaded.ItemId!.Value);

        PlatformInboxItem item = await _db.PlatformInboxItems.SingleAsync();
        item.Status.ShouldBe(PlatformInboxStatus.Assigned);
        item.MatchedBy.ShouldBe(PlatformInboxMatch.Commission);
        item.PfaRegistrationId.ShouldBe(_maria);
        (await _db.PlatformDocuments.SingleAsync(d => d.Id == item.PlatformDocumentId)).DocumentType.ShouldBe(PlatformDocumentType.Unknown);
    }

    [Fact]
    public async Task Scanned_document_is_assigned_by_the_cui_the_ai_read()
    {
        // Fără text layer: nici CUI în text, nici în numele fișierului; AI-ul citește paginile ca imagine.
        Guid id = (await Upload(("scan001.pdf", string.Empty))).Single().ItemId!.Value;
        _extractor.Next = Reading(PlatformDocumentType.CommissionInvoice, 300m) with { CustomerTaxId = $"RO {MariaCui}" };
        await Match(id);

        PlatformInboxItem item = await _db.PlatformInboxItems.SingleAsync();
        item.Status.ShouldBe(PlatformInboxStatus.Assigned);
        item.MatchedBy.ShouldBe(PlatformInboxMatch.Cui);
        item.PfaRegistrationId.ShouldBe(_maria);
    }

    [Fact]
    public async Task Bolt_report_is_assigned_by_the_driver_name_the_ai_read()
    {
        Guid id = (await Upload(("rezumat.pdf", "Rezumat lunar"))).Single().ItemId!.Value;
        _extractor.Next = Reading(PlatformDocumentType.PlatformReport, 300m) with { CustomerName = "ION POPESCU" };
        await Match(id);

        PlatformInboxItem item = await _db.PlatformInboxItems.SingleAsync();
        item.Status.ShouldBe(PlatformInboxStatus.Assigned);
        item.MatchedBy.ShouldBe(PlatformInboxMatch.Name);
        item.PfaRegistrationId.ShouldBe(_ion);
    }

    [Fact]
    public async Task Cui_read_by_the_ai_that_is_not_a_client_is_reported()
    {
        Guid id = (await Upload(("scan002.pdf", string.Empty))).Single().ItemId!.Value;
        _extractor.Next = Reading(PlatformDocumentType.CommissionInvoice, 300m) with { CustomerName = "Altcineva PFA", CustomerTaxId = StrangerCui };
        await Match(id);

        PlatformInboxItemDto item = (await Inbox()).Single();
        item.Status.ShouldBe(PlatformInboxStatus.UnknownCui);
        item.DetectedCui.ShouldBe(StrangerCui);
    }

    [Fact]
    public async Task A_match_in_a_closed_month_needs_review_instead_of_waiting_forever()
    {
        _db.PfaAccountingPeriods.Add(new PfaAccountingPeriod { Id = Guid.NewGuid(), PfaRegistrationId = _maria, Period = Period, Status = AccountingPeriodStatus.Closed });
        await _db.SaveChangesAsync();
        Guid id = (await Upload(("scan003.pdf", string.Empty))).Single().ItemId!.Value;
        _extractor.Next = Reading(PlatformDocumentType.CommissionInvoice, 300m) with { CustomerTaxId = MariaCui };
        await Match(id);

        PlatformInboxItemDto item = (await Inbox()).Single();
        item.Status.ShouldBe(PlatformInboxStatus.NeedsReview);
        item.PfaId.ShouldBe(_maria);
    }

    [Fact]
    public async Task Same_commission_at_two_clients_needs_review()
    {
        await ClientInvoice(_ion, 750m);
        await ClientInvoice(_maria, 750m);

        Guid id = (await Upload(("raport.pdf", "Raport Bolt\nComision 750,00"))).Single().ItemId!.Value;
        _extractor.Next = Reading(PlatformDocumentType.PlatformReport, 750m);
        await Match(id);

        PlatformInboxItemDto item = (await Inbox()).Single();
        item.Status.ShouldBe(PlatformInboxStatus.NeedsReview);
        item.Reason.ShouldBe("De verificat: același comision la mai mulți clienți.");
    }

    [Fact]
    public async Task No_cui_and_no_commission_match_needs_review_and_admin_assigns_it()
    {
        Guid id = (await Upload(("raport.pdf", "Raport Bolt\nComision 300,00"))).Single().ItemId!.Value;
        _extractor.Next = Reading(PlatformDocumentType.PlatformReport, 300m);
        await Match(id);

        PlatformInboxItemDto item = (await Inbox()).Single();
        item.Status.ShouldBe(PlatformInboxStatus.NeedsReview);
        item.Reason!.ShouldStartWith("De verificat");

        (await new AssignPlatformInboxItemCommandHandler(_db, User()).Handle(new AssignPlatformInboxItemCommand(id, _ion), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        PlatformInboxItem assigned = await _db.PlatformInboxItems.SingleAsync();
        assigned.MatchedBy.ShouldBe(PlatformInboxMatch.Manual);
        assigned.ResolvedByUserId.ShouldBe(_admin);
        (await _db.PlatformDocuments.SingleAsync()).PfaRegistrationId.ShouldBe(_ion);
        (await Inbox()).ShouldBeEmpty();
        (await new DismissPlatformInboxItemCommandHandler(_db, User()).Handle(new DismissPlatformInboxItemCommand(id), CancellationToken.None))
            .Error.Code.ShouldBe("Accounting.InboxItemResolved");
    }

    [Fact]
    public async Task Unreadable_document_needs_review_and_can_be_dismissed()
    {
        Guid id = (await Upload(("scan.pdf", "imagine"))).Single().ItemId!.Value;
        _extractor.Fail = true;
        await Match(id);

        (await Inbox()).Single().Reason.ShouldBe("De verificat: Serviciul AI nu răspunde.");

        (await new DismissPlatformInboxItemCommandHandler(_db, User()).Handle(new DismissPlatformInboxItemCommand(id), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await Inbox()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Duplicates_are_refused_in_the_batch_and_against_documents_already_uploaded()
    {
        string text = $"CIF: {IonCui}\nComision 100,00";

        IReadOnlyList<PlatformInboxResultDto> first = await Upload(("a.pdf", text), ("b.pdf", text), ("note.txt", "x"));
        first[0].Status.ShouldBe(PlatformInboxStatus.Assigned);
        first[1].Message.ShouldStartWith("Duplicat");
        first[2].Message.ShouldBe("Doar fișiere PDF.");

        IReadOnlyList<PlatformInboxResultDto> again = await Upload(("c.pdf", text));
        again.Single().Message.ShouldBe("Duplicat: fișierul e deja încărcat la Popescu Ion PFA.");
        (await _db.PlatformDocuments.CountAsync()).ShouldBe(1);
    }

    public void Dispose() => _db.Dispose();

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private void AddPfa(Guid id, string cui, string first, string last)
    {
        var owner = new User { Id = Guid.NewGuid(), Email = $"{id:N}@ridelance.ro", FirstName = first, LastName = last, Role = UserRole.Client };
        _db.Users.Add(owner);
        _db.PfaRegistrations.Add(new PfaRegistration { Id = id, UserId = owner.Id, User = owner, Cui = cui, LegalName = $"{last} {first} PFA" });
    }

    /// <summary>Factura de comision Bolt a clientului, citită, cu comisionul dat.</summary>
    private async Task ClientInvoice(Guid pfaId, decimal commission)
    {
        Guid ownerId = await _db.PfaRegistrations.Where(p => p.Id == pfaId).Select(p => p.UserId).SingleAsync();
        var document = new PlatformDocument
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaId,
            Period = Period,
            Platform = Platform.Bolt,
            DocumentType = PlatformDocumentType.CommissionInvoice,
            Status = PlatformDocumentStatus.Confirmed,
            SourceDocumentId = Guid.NewGuid(),
            FileHash = Guid.NewGuid().ToString("N"),
            UploadedByUserId = ownerId,
        };
        _db.PlatformDocuments.Add(document);
        _db.DocumentExtractions.Add(new DocumentExtraction
        {
            Id = Guid.NewGuid(),
            PlatformDocumentId = document.Id,
            Version = 1,
            IsCurrent = true,
            CommissionAmount = commission,
        });
        await _db.SaveChangesAsync();
    }

    private static DocumentExtractionResult Reading(PlatformDocumentType type, decimal commission) => new(
        type,
        Platform.Bolt,
        new ExtractedFields("Bolt Operations OÜ", "EE", "EE102090374", null, new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "RON", 5000m, commission, []),
        new Dictionary<string, string>(),
        0.95,
        "fake-extractor",
        "test");

    /// <summary>„PDF-ul” e chiar textul lui, ca extractorul de text fals să-l poată întoarce.</summary>
    private async Task<IReadOnlyList<PlatformInboxResultDto>> Upload(params (string Name, string Text)[] files)
    {
        Result<IReadOnlyList<PlatformInboxResultDto>> result = await new UploadPlatformInboxCommandHandler(_db, _files, new TextOfBytes(), User(), _options).Handle(
            new UploadPlatformInboxCommand(Period, [.. files.Select(f => new InboxUploadFile(f.Name, f.Name.EndsWith(".pdf", StringComparison.Ordinal) ? "application/pdf" : "text/plain", Encoding.UTF8.GetBytes(f.Text)))]),
            CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Description : null);
        return result.Value;
    }

    private Task<Result> Match(Guid id) =>
        new RunPlatformInboxMatchingCommandHandler(_db, _files, new TextOfBytes(), _extractor).Handle(new RunPlatformInboxMatchingCommand(id), CancellationToken.None);

    private async Task<IReadOnlyList<PlatformInboxItemDto>> Inbox() =>
        (await new GetPlatformInboxQueryHandler(_db).Handle(new GetPlatformInboxQuery(), CancellationToken.None)).Value;

    private FixedUser User() => new(_admin);

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class FakeExtractor : IDocumentExtractor
    {
        public DocumentExtractionResult? Next { get; set; }

        public bool Fail { get; set; }

        public Task<Result<DocumentExtractionResult>> ExtractAsync(DocumentExtractionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Fail
                ? Result.Failure<DocumentExtractionResult>(Error.Failure("Ai.RequestFailed", "Serviciul AI nu răspunde."))
                : Result.Success(Next!));
    }

    private sealed class TextOfBytes : IPdfTextExtractor
    {
        public string? ExtractText(byte[] pdfBytes) => Encoding.UTF8.GetString(pdfBytes);
    }

    private sealed class MemoryEncryption : IFileEncryptionService
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public async Task<EncryptedFileResult> EncryptAndSaveAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await fileStream.CopyToAsync(buffer, cancellationToken);
            _files[fileName] = buffer.ToArray();
            return new EncryptedFileResult(fileName, "iv");
        }

        public Task<Stream> DecryptAndReadAsync(string encryptedFilePath, string iv, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(_files[encryptedFilePath]));
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

using System.Text;
using System.Text.Json;
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
/// B1 cap-coadă, cu un extractor fals care reproduce cazurile din fixtures: upload → extracție →
/// verificări → editare cu motiv → confirmare; duplicatul, confirmarea în bloc, citirea eșuată.
/// </summary>
public sealed class PlatformDocumentFlowTests : IDisposable
{
    private const string Period = "2026-08";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _accountant = Guid.NewGuid();
    private readonly Guid _pfa = Guid.NewGuid();
    private readonly FakeExtractor _extractor = new();
    private readonly MemoryEncryption _files = new();
    private readonly IOptions<AccountingOptions> _options = Options.Create(new AccountingOptions());

    public PlatformDocumentFlowTests()
    {
        var owner = new User { Id = Guid.NewGuid(), Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu", Role = UserRole.Client };
        _db.Users.Add(owner);
        _db.Users.Add(new User { Id = _accountant, Email = "contabil@ridelance.ro", FirstName = "Contabil", LastName = "RIDElance", Role = UserRole.Contabil });
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = owner.Id, Cui = "41000001" });
        _db.SupplierTaxProfiles.Add(new SupplierTaxProfile
        {
            Id = Guid.NewGuid(),
            SupplierName = "Bolt Operations OÜ",
            Country = "EE",
            VatId = "EE102090374",
            D100Rate = 2,
            D100RateConfirmed = true,
            ValidFrom = new DateOnly(2025, 1, 1),
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Clean_invoice_goes_from_upload_to_confirmed_with_audit()
    {
        Guid id = await UploadAndExtract("bolt.pdf", BoltText("1.000,00"), Bolt(1000m));

        PlatformDocumentDetail detail = await Get(id);
        detail.Status.ShouldBe(PlatformDocumentStatus.PendingConfirmation);
        detail.Platform.ShouldBe(Platform.Bolt);
        detail.DocumentType.ShouldBe(PlatformDocumentType.CommissionInvoice);
        detail.Extraction!.Fields.CommissionAmount.ShouldBe(1000m);
        detail.Extraction.SourceSnippets["commissionAmount"].ShouldBe("1.000,00");

        Result<PlatformDocumentDetail> confirmed = await Confirm(id);

        confirmed.Value.Status.ShouldBe(PlatformDocumentStatus.Confirmed);
        confirmed.Value.ReviewedBy!.Name.ShouldBe("Contabil RIDElance");
        (await _db.AuditLogs.Select(a => a.Action).ToListAsync()).ShouldBe(["UPLOAD", "CONFIRM"], ignoreOrder: true);
    }

    [Fact]
    public async Task Same_file_for_the_same_pfa_is_a_duplicate_pointing_to_the_first()
    {
        Guid first = await UploadAndExtract("bolt.pdf", BoltText("1.000,00"), Bolt(1000m));

        Result<PlatformDocumentDto> second = await Upload("copie.pdf", BoltText("1.000,00"));

        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldBeOfType<DuplicatePlatformDocumentError>().ExistingDocumentId.ShouldBe(first);
        second.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Misread_amount_needs_review_until_a_manual_edit_with_reason()
    {
        // Bogdan Matei: modelul citește 1.284,50, în PDF scrie 1.248,50.
        Guid id = await UploadAndExtract("bolt.pdf", BoltText("1.248,50"), Bolt(1284.50m));
        PlatformDocumentDetail detail = await Get(id);
        detail.Status.ShouldBe(PlatformDocumentStatus.NeedsReview);
        detail.Checks.Where(c => !c.Passed).Select(c => c.Code).ShouldBe([DocumentCheckCode.AmountInText]);

        (await Confirm(id)).Error.Code.ShouldBe(AccountingErrors.ChecksFailed.Code);
        (await Edit(id, """{"commissionAmount":1248.50,"amount":1248.50}""", "  ")).Error.ShouldBe(AccountingErrors.ReasonRequired);

        Result<PlatformDocumentDetail> edited = await Edit(id, """{"commissionAmount":1248.50,"amount":1248.50}""", "Suma corectă din PDF");

        edited.Value.Status.ShouldBe(PlatformDocumentStatus.PendingConfirmation);
        edited.Value.Extraction!.Version.ShouldBe(2);
        edited.Value.Extraction.IsManualEdit.ShouldBeTrue();
        edited.Value.Extraction.ManuallyEditedFields.ShouldBe(["amount", "commissionAmount"], ignoreOrder: true);
        edited.Value.Extraction.SourceSnippets.ShouldNotContainKey("commissionAmount");
        AuditLog audit = await _db.AuditLogs.SingleAsync(a => a.Action == "MANUAL_EDIT");
        audit.Reason.ShouldBe("Suma corectă din PDF");
        audit.BeforeJson!.ShouldContain("1284.5");

        (await Confirm(id)).Value.Status.ShouldBe(PlatformDocumentStatus.Confirmed);
    }

    [Fact]
    public async Task Unknown_supplier_resolves_once_it_is_added_to_the_registry()
    {
        // Răzvan Ene: cod TVA Uber necunoscut.
        Guid id = await UploadAndExtract("uber.pdf", UberText("NL001234567B01"), Uber("NL001234567B01"));
        PlatformDocumentDetail detail = await Get(id);
        detail.Status.ShouldBe(PlatformDocumentStatus.NeedsReview);
        detail.Checks.Single(c => !c.Passed).Action.ShouldBe(DocumentCheckAction.AddSupplier);

        _db.SupplierTaxProfiles.Add(new SupplierTaxProfile
        {
            Id = Guid.NewGuid(),
            SupplierName = "Uber B.V.",
            Country = "NL",
            VatId = "NL001234567B01",
            ValidFrom = new DateOnly(2025, 1, 1),
        });
        await _db.SaveChangesAsync();

        (await Get(id)).Status.ShouldBe(PlatformDocumentStatus.PendingConfirmation);
    }

    [Fact]
    public async Task Bulk_confirmation_reports_what_it_skipped_and_why()
    {
        Guid clean = await UploadAndExtract("bolt.pdf", BoltText("1.000,00"), Bolt(1000m));
        // Altă factură (alt număr), ca să nu fie prinsă de NOT_DUPLICATE.
        Guid misread = await UploadAndExtract("bolt2.pdf", BoltText("1.248,50"), Bolt(1284.50m, "EE-BOLT-2026-08-2000"));
        var missing = Guid.NewGuid();

        ConfirmBulkResult result = (await new ConfirmPlatformDocumentsBulkCommandHandler(_db, User(), _options)
            .Handle(new ConfirmPlatformDocumentsBulkCommand([clean, misread, missing]), CancellationToken.None)).Value;

        result.Confirmed.ShouldBe([clean]);
        result.Skipped.Select(s => s.Id).ShouldBe([misread, missing]);
        result.Skipped[0].Reason.ShouldContain("nu apare în textul documentului");
    }

    [Fact]
    public async Task Failed_extraction_is_visible_on_the_document()
    {
        _extractor.Fail = true;
        Guid id = await UploadAndExtract("bolt.pdf", BoltText("1.000,00"), Bolt(1000m));

        PlatformDocumentDetail detail = await Get(id);
        detail.Status.ShouldBe(PlatformDocumentStatus.ExtractionFailed);
        detail.ExtractionError.ShouldBe("Serviciul AI nu răspunde.");
        detail.Extraction.ShouldBeNull();
    }

    [Fact]
    public async Task Upload_rejects_a_malformed_period()
    {
        Result<PlatformDocumentDto> result = await new UploadPlatformDocumentCommandHandler(_db, _files, User(), _options).Handle(
            new UploadPlatformDocumentCommand(_pfa, "08-2026", "x.pdf", "application/pdf", new MemoryStream([1]), 1),
            CancellationToken.None);

        result.Error.ShouldBe(AccountingErrors.InvalidPeriod);
    }

    public void Dispose() => _db.Dispose();

    // ─── Ajutoare ──────────────────────────────────────────────────────────────────────────────

    private static string BoltText(string commission) =>
        $"Bolt Operations OÜ\nCod TVA: EE102090374\nFactură EE-BOLT-2026-08-1000\nData: 31.08.2026\nComision: {commission} RON\nTVA: 0,00 RON\nTotal: {commission} RON";

    private static string UberText(string vatId) =>
        $"Uber B.V.\nCod TVA: {vatId}\nFactură UBR-RO-2026-08-1002\nComision: 600,00 RON\nTVA: 0,00 RON\nTotal: 600,00 RON";

    private static DocumentExtractionResult Bolt(decimal commission, string invoiceNumber = "EE-BOLT-2026-08-1000") => new(
        PlatformDocumentType.CommissionInvoice,
        Platform.Bolt,
        new ExtractedFields("Bolt Operations OÜ", "EE", "EE102090374", invoiceNumber, new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "RON", commission, commission, [new OtherAmount("TVA", 0)]),
        new Dictionary<string, string> { ["commissionAmount"] = commission.ToString("#,##0.00", new System.Globalization.CultureInfo("ro-RO")) },
        0.97,
        "fake-extractor",
        "test");

    private static DocumentExtractionResult Uber(string vatId) => new(
        PlatformDocumentType.CommissionInvoice,
        Platform.Uber,
        new ExtractedFields("Uber B.V.", "NL", vatId, "UBR-RO-2026-08-1002", new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), "RON", 600m, 600m, [new OtherAmount("TVA", 0)]),
        new Dictionary<string, string>(),
        0.95,
        "fake-extractor",
        "test");

    private async Task<Guid> UploadAndExtract(string fileName, string pdfText, DocumentExtractionResult reading)
    {
        _extractor.Next = reading;
        Result<PlatformDocumentDto> uploaded = await Upload(fileName, pdfText);
        uploaded.IsSuccess.ShouldBeTrue(uploaded.IsFailure ? uploaded.Error.Description : null);
        uploaded.Value.Status.ShouldBe(PlatformDocumentStatus.Extracting);

        await new RunPlatformDocumentExtractionCommandHandler(_db, _files, new TextOfBytes(), _extractor, _options)
            .Handle(new RunPlatformDocumentExtractionCommand(uploaded.Value.Id), CancellationToken.None);
        return uploaded.Value.Id;
    }

    /// <summary>„PDF-ul” e chiar textul lui, ca extractorul de text fals să-l poată întoarce.</summary>
    private Task<Result<PlatformDocumentDto>> Upload(string fileName, string pdfText)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(pdfText);
        return new UploadPlatformDocumentCommandHandler(_db, _files, User(), _options).Handle(
            new UploadPlatformDocumentCommand(_pfa, Period, fileName, "application/pdf", new MemoryStream(bytes), bytes.Length),
            CancellationToken.None);
    }

    private async Task<PlatformDocumentDetail> Get(Guid id) =>
        (await new GetPlatformDocumentQueryHandler(_db, _options).Handle(new GetPlatformDocumentQuery(id), CancellationToken.None)).Value;

    private Task<Result<PlatformDocumentDetail>> Confirm(Guid id) =>
        new ConfirmPlatformDocumentCommandHandler(_db, User(), _options).Handle(new ConfirmPlatformDocumentCommand(id), CancellationToken.None);

    private Task<Result<PlatformDocumentDetail>> Edit(Guid id, string fields, string reason)
    {
        using var json = JsonDocument.Parse(fields);
        return new UpdateExtractionCommandHandler(_db, User(), _options)
            .Handle(new UpdateExtractionCommand(id, json.RootElement.Clone(), reason), CancellationToken.None);
    }

    private FixedUser User() => new(_accountant);

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

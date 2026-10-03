using Application.Abstractions.Authentication;
using Application.Accounting.Ledger;
using Application.Accounting.NonResident;
using Application.Accounting.Tax;
using Domain.Accounting;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>Spec declarații F20–F25: plata către nerezident din decontare, decizia și confirmarea Adminului.</summary>
public sealed class NonResidentTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly Guid _pfa = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly SupplierTaxProfile _bolt;

    public NonResidentTests()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu" };
        _db.Users.Add(new User { Id = _admin, Email = "admin@ridelance.ro", FirstName = "Admin", LastName = "RIDElance" });
        _db.PfaRegistrations.Add(new PfaRegistration { Id = _pfa, UserId = user.Id, User = user, FullName = "Ion Popescu", Cui = "12345674" });
        _db.TaxRules.AddRange(TaxRuleSeed.Rules);
        _bolt = new SupplierTaxProfile
        {
            Id = Guid.NewGuid(), SupplierName = "Bolt Operations OÜ", Country = "EE", VatId = "EE102090374", Treaty = "Convenția RO–EE",
            D100Rate = 2, D100RateConfirmed = true, ValidFrom = new DateOnly(2025, 1, 1),
            ResidenceCertValidFrom = new DateOnly(2026, 1, 1), ResidenceCertValidTo = new DateOnly(2026, 12, 31),
        };
        _db.SupplierTaxProfiles.Add(_bolt);

        // Septembrie: raportul și factura de comision; comisionul se reține la decontarea din 05.10.
        Guid report = Document(PlatformDocumentType.PlatformReport, null);
        Document(PlatformDocumentType.CommissionInvoice, "EE102090374");
        _db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Date = new DateOnly(2026, 10, 5), DocumentLabel = "Extras", Source = LedgerSource.Bolt,
            Description = "Comision Bolt", TransactionType = LedgerTransactionType.Expense, PaymentMethod = PaymentMethod.Bank, Amount = -350m,
            Category = LedgerSupport.PlatformCommissionCategory, SettlementGroupId = Guid.NewGuid(), PlatformDocumentId = report,
            Status = LedgerEntryStatus.Verified, AccountingPeriod = "2026-10",
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    /// <summary>F20, F21: plata are data decontării și furnizorul juridic din factură; cota din tratat, codul 634.</summary>
    [Fact]
    public async Task F20_F21_TheSettlementCommissionIsAPaymentOnItsSettlementDate()
    {
        await NonResidentSync.SyncAsync(_db, _pfa, CancellationToken.None);
        await NonResidentSync.SyncAsync(_db, _pfa, CancellationToken.None);

        NonResidentPayment payment = await _db.NonResidentPayments.SingleAsync();
        (payment.PaymentDate, payment.SupplierTaxId, payment.SupplierCountry, payment.GrossIncomeRon).ShouldBe((new DateOnly(2026, 10, 5), "EE102090374", "EE", 350m));
        NonResidentTaxDecision decision = await _db.NonResidentTaxDecisions.SingleAsync();
        (decision.TaxRate, decision.TaxDue, decision.ObligationCode, decision.Status).ShouldBe((2m, 7m, "634", NonResidentDecisionStatus.Auto));

        (await NonResidentSync.LinesAsync(_db, [_pfa], new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), CancellationToken.None))[_pfa].ShouldBeEmpty();
        (await NonResidentSync.LinesAsync(_db, [_pfa], new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None))[_pfa].ShouldHaveSingleItem();
    }

    /// <summary>F23: certificat expirat → în coada de confirmare; după confirmarea Adminului, decizia nu se mai schimbă.</summary>
    [Fact]
    public async Task F23_AnAdminConfirmationFreezesTheDecision()
    {
        _bolt.ResidenceCertValidTo = new DateOnly(2026, 9, 30);
        await _db.SaveChangesAsync();

        IReadOnlyList<NonResidentDecisionDto> queue = (await new ListNonResidentDecisionsQueryHandler(_db)
            .Handle(new ListNonResidentDecisionsQuery(_pfa, NonResidentDecisionStatus.NeedsLegalConfirmation), CancellationToken.None)).Value;
        NonResidentDecisionDto pending = queue.ShouldHaveSingleItem();
        (pending.TaxRate, pending.TaxDue).ShouldBe((16m, 56m));

        var confirm = new ConfirmNonResidentDecisionCommandHandler(_db, new FixedUser(_admin));
        (await confirm.Handle(new ConfirmNonResidentDecisionCommand(pending.Id, " "), CancellationToken.None)).Error.Code.ShouldBe("Accounting.ReasonRequired");
        (await confirm.Handle(new ConfirmNonResidentDecisionCommand(pending.Id, "Certificat nou cerut; aplicăm cota legală"), CancellationToken.None))
            .Value.Status.ShouldBe(NonResidentDecisionStatus.Confirmed);

        _bolt.ResidenceCertValidTo = new DateOnly(2026, 12, 31);
        await _db.SaveChangesAsync();
        await NonResidentSync.SyncAsync(_db, _pfa, CancellationToken.None);
        NonResidentTaxDecision frozen = await _db.NonResidentTaxDecisions.SingleAsync();
        (frozen.Status, frozen.TaxRate).ShouldBe((NonResidentDecisionStatus.Confirmed, 16m));
    }

    private Guid Document(PlatformDocumentType type, string? vatId)
    {
        var file = new Document { Id = Guid.NewGuid(), OriginalFileName = $"{type}.pdf", ContentType = "application/pdf", Origin = DocumentOrigin.AccountingUpload };
        var document = new PlatformDocument
        {
            Id = Guid.NewGuid(), PfaRegistrationId = _pfa, Period = "2026-09", Platform = Platform.Bolt, DocumentType = type,
            SourceDocumentId = file.Id, FileHash = Guid.NewGuid().ToString("N"), Status = PlatformDocumentStatus.Confirmed,
        };
        _db.Documents.Add(file);
        _db.PlatformDocuments.Add(document);
        _db.DocumentExtractions.Add(new DocumentExtraction
        {
            Id = Guid.NewGuid(), PlatformDocumentId = document.Id, Version = 1, IsCurrent = true,
            SupplierName = "Bolt Operations OÜ", SupplierCountry = "EE", SupplierVatId = vatId, TaxPointDate = new DateOnly(2026, 9, 30),
        });
        return document.Id;
    }

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

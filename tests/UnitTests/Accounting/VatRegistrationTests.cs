using System.Text;
using Application.Abstractions.Anaf;
using Application.Abstractions.Authentication;
using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Declarations;
using Application.Accounting.Months;
using Application.Accounting.Pfas;
using Application.Accounting.VatRegistration;
using Domain.Accounting;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Accounting.Anaf;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Cererea D700 pentru codul de TVA art. 317: generată din datele PFA-ului când clientul răspunde
/// „Nu” în onboarding, verificată cu validatorul ANAF, aprobată de contabil, închisă cu codul primit.
/// </summary>
public sealed class VatRegistrationTests : IDisposable
{
    /// <summary>XML-ul trecut de validatorul ANAF oficial (D700Validator J5.0.3), cu PDF generat.</summary>
    private const string AcceptedByAnaf =
        """<D700 xmlns="mfp:anaf:dgti:d700:declaratie:v4" an="2026" luna="9" nume_decl="POPESCU" pren_decl="ION" func_decl="TITULAR" totalPlata_A="1" felD="2" cif="12345674" den="POPESCU ION PFA" dec_inreg="070" Bifa_III="0" Bifa_A="0" Bifa_B="1" Bifa_C="0" Bifa_D="0" Bifa_E="0" Bifa_F="0" Bifa_G="0" Bifa_B1="0" Bifa_B2="0" Bifa_B3="0" Bifa_B4="0" Bifa_B5="0" Bifa_B6="1" Bifa_B7="0" Bifa_B8="0" Bifa_6b="1" Bifa_6b_abc="1" Bifa1_6b="1" Bifa_6b_inreg="3" />""";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());

    private readonly MemoryFiles _files = new();
    private readonly FakeAnafValidator _anaf = new();
    private readonly Guid _accountant = Guid.NewGuid();
    private readonly Guid _pfa;

    public VatRegistrationTests()
    {
        _db.Users.Add(new User { Id = _accountant, Email = "contabil@ridelance.ro", FirstName = "Contabil", LastName = "RIDElance", Role = UserRole.Contabil });
        var user = new User { Id = Guid.NewGuid(), Email = "ion@ridelance.ro", FirstName = "Ion", LastName = "Popescu", Role = UserRole.Client };
        var pfa = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            FullName = "Ion Popescu",
            LegalName = "POPESCU ION PFA",
            Cui = "12345674",
            AssignedContabilId = _accountant,
        };
        _pfa = pfa.Id;
        _db.PfaRegistrations.Add(pfa);
        _db.PfaFiscalProfiles.Add(new PfaFiscalProfile
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa.Id,
            VatAnswer = VatAnswer.No,
            VatRegistrationKind = VatRegistrationKind.None,
            SpecialVatCodeStatus = PfaSpecialVatCodeStatus.No,
        });
        _db.AnafDeclarationSchemas.Add(new AnafDeclarationSchema
        {
            Id = Guid.NewGuid(),
            DeclarationType = DeclarationType.D100,
            Version = "v2-20220224",
            ValidatorVersion = "2026-09",
            ValidFrom = new DateOnly(2025, 1, 1),
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void The_xml_is_the_one_accepted_by_the_ANAF_validator()
    {
        byte[] xml = new D700Xml().Build(new D700Content("2026-09", "12345674", "POPESCU ION PFA", "Popescu", "Ion", "TITULAR"));

        System.Xml.Linq.XElement built = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(xml)).Root!;
        var accepted = System.Xml.Linq.XElement.Parse(AcceptedByAnaf);
        built.Name.ShouldBe(accepted.Name);
        built.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => $"{a.Name.LocalName}={a.Value.ToUpperInvariant()}")
            .ShouldBe(accepted.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => $"{a.Name.LocalName}={a.Value}"));
    }

    [Fact]
    public async Task F01_F02_No_generates_the_D700_from_the_pfa_data_once()
    {
        VatRegistrationRequest request = (await Service().EnsureRequestedAsync(_pfa, null, CancellationToken.None)).Value;

        request.Status.ShouldBe(VatRegistrationStatus.Generated);
        string xml = Xml(request);
        xml.ShouldContain("cif=\"12345674\"");
        xml.ShouldContain("den=\"POPESCU ION PFA\"");
        xml.ShouldContain("Bifa_6b_inreg=\"3\"");

        VatRegistrationRequest again = (await Service().EnsureRequestedAsync(_pfa, null, CancellationToken.None)).Value;
        again.Id.ShouldBe(request.Id);
        again.XmlDocumentId.ShouldBe(request.XmlDocumentId);
        (await _db.VatRegistrationRequests.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_cui_it_waits_and_generates_once_the_cui_exists()
    {
        PfaRegistration pfa = await _db.PfaRegistrations.SingleAsync(p => p.Id == _pfa);
        pfa.Cui = null;
        await _db.SaveChangesAsync();

        VatRegistrationRequest waiting = (await Service().EnsureRequestedAsync(_pfa, null, CancellationToken.None)).Value;
        waiting.Status.ShouldBe(VatRegistrationStatus.WaitingForData);
        waiting.MissingData.ShouldBe("Lipsește CUI-ul PFA-ului.");
        waiting.XmlDocumentId.ShouldBeNull();

        pfa.Cui = "12345674";
        await _db.SaveChangesAsync();
        VatRegistrationRequest generated = (await Service().EnsureRequestedAsync(_pfa, null, CancellationToken.None)).Value;
        generated.Id.ShouldBe(waiting.Id);
        generated.Status.ShouldBe(VatRegistrationStatus.Generated);
        generated.MissingData.ShouldBeNull();
    }

    [Fact]
    public async Task F03_TheCodeIsActiveOnlyAfterSubmissionAndTheFiscalVectorProof()
    {
        VatRegistrationService service = Service();
        Guid id = (await service.EnsureRequestedAsync(_pfa, null, CancellationToken.None)).Value.Id;

        VatRegistrationRequest validated = (await service.ValidateAsync(id, _accountant, CancellationToken.None)).Value;
        validated.Status.ShouldBe(VatRegistrationStatus.ReadyForReview);
        validated.PdfDocumentId.ShouldNotBeNull();
        _anaf.OtherCalls.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            call => call.Type.ShouldBe("D700"),
            call => call.Version.ShouldBe("2026-09"));

        (await service.TransitionAsync(id, VatRegistrationStatus.Approved, null, _accountant, CancellationToken.None)).Value.Status.ShouldBe(VatRegistrationStatus.Approved);
        (await service.GenerateAsync(_pfa, _accountant, CancellationToken.None)).Error.ShouldBe(VatRegistrationErrors.Locked);
        var certificate = new VatCertificateFile("certificat.pdf", "application/pdf", "%PDF-1.4 certificat"u8.ToArray());

        // F03: nu înainte de depunere și nu fără dovada din vectorul fiscal.
        (await service.RegisterAsync(id, "RO12345674", new DateOnly(2026, 10, 1), certificate, _accountant, CancellationToken.None))
            .Error.Code.ShouldBe("VatRegistration.WrongStatus");
        (await service.TransitionAsync(id, VatRegistrationStatus.Submitted, null, _accountant, CancellationToken.None)).Value.Status.ShouldBe(VatRegistrationStatus.Submitted);
        (await service.RegisterAsync(id, "RO12345674", new DateOnly(2026, 10, 1), null, _accountant, CancellationToken.None))
            .Error.ShouldBe(VatRegistrationErrors.CertificateRequired);

        VatRegistrationRequest registered = (await service.RegisterAsync(id, "ro 1234 5674", new DateOnly(2026, 10, 1), certificate, _accountant, CancellationToken.None)).Value;
        registered.Status.ShouldBe(VatRegistrationStatus.Registered);
        registered.VatCode.ShouldBe("RO12345674");

        PfaAccountingSetting code = await _db.PfaAccountingSettings.SingleAsync(s => s.PfaRegistrationId == _pfa && s.Key == PfaAccountingSettingKeys.Art317VatCode);
        code.ValueJson.ShouldBe("\"RO12345674\"");
        code.ValidFrom.ShouldBe(new DateOnly(2026, 10, 1));
        (await _db.PfaAccountingSettings.SingleAsync(s => s.PfaRegistrationId == _pfa && s.Key == PfaAccountingSettingKeys.Art317)).ValueJson.ShouldBe("true");

        PfaFiscalProfile profile = await _db.PfaFiscalProfiles.SingleAsync(p => p.PfaRegistrationId == _pfa);
        profile.SpecialVatCodeStatus.ShouldBe(PfaSpecialVatCodeStatus.Yes);
        profile.VatRegistrationKind.ShouldBe(VatRegistrationKind.SpecialArticle317);
        profile.SpecialVatCodeDocumentId.ShouldBe(registered.CertificateDocumentId);
        (await _db.Documents.SingleAsync(d => d.Id == registered.CertificateDocumentId)).Category.ShouldBe(DocumentCategory.CertificatTvaIntracomunitar);
    }

    [Fact]
    public async Task Anaf_errors_fail_the_validation_and_a_rejection_needs_a_reason()
    {
        VatRegistrationService service = Service();
        Guid id = (await service.EnsureRequestedAsync(_pfa, null, CancellationToken.None)).Value.Id;
        _anaf.RespondOther = (_, _) => new AnafValidatorResult(false, [new AnafValidatorMessage("R7.2", "CUI invalid", "cif", null)], [], "E", null, 900, "test");

        VatRegistrationRequest failed = (await service.ValidateAsync(id, _accountant, CancellationToken.None)).Value;
        failed.Status.ShouldBe(VatRegistrationStatus.ValidationFailed);
        failed.PdfDocumentId.ShouldBeNull();
        failed.ValidationJson!.ShouldContain("cif: CUI invalid");
        (await service.TransitionAsync(id, VatRegistrationStatus.Approved, null, _accountant, CancellationToken.None)).IsFailure.ShouldBeTrue();

        (await service.TransitionAsync(id, VatRegistrationStatus.Rejected, " ", _accountant, CancellationToken.None)).Error.ShouldBe(VatRegistrationErrors.ReasonRequired);
        VatRegistrationRequest rejected = (await service.TransitionAsync(id, VatRegistrationStatus.Rejected, "Nume greșit", _accountant, CancellationToken.None)).Value;
        rejected.RejectionReason.ShouldBe("Nume greșit");

        // Respinsă: se regenerează din datele corectate.
        (await service.GenerateAsync(_pfa, _accountant, CancellationToken.None)).Value.Status.ShouldBe(VatRegistrationStatus.Generated);
    }

    [Fact]
    public async Task The_list_shows_the_latest_request_of_the_accountants_clients()
    {
        await Service().EnsureRequestedAsync(_pfa, null, CancellationToken.None);

        IReadOnlyList<VatRegistrationDto> mine = (await new ListVatRegistrationsQueryHandler(_db, new FixedUser(_accountant))
            .Handle(new ListVatRegistrationsQuery(), CancellationToken.None)).Value;
        VatRegistrationDto row = mine.ShouldHaveSingleItem();
        row.ClientName.ShouldBe("POPESCU ION PFA");
        row.Status.ShouldBe(VatRegistrationStatus.Generated);
        row.HasXml.ShouldBeTrue();

        var other = Guid.NewGuid();
        _db.Users.Add(new User { Id = other, Email = "alt@ridelance.ro", FirstName = "Alt", LastName = "Contabil", Role = UserRole.Contabil });
        await _db.SaveChangesAsync();
        (await new ListVatRegistrationsQueryHandler(_db, new FixedUser(other)).Handle(new ListVatRegistrationsQuery(), CancellationToken.None)).Value.ShouldBeEmpty();
    }

    /// <summary>F04: procesarea lunii pe un PFA fără art. 317 activ oprește D301/D390 și creează task-ul D700.</summary>
    [Fact]
    public async Task F04_ProcessingAMonthWithoutArt317CreatesTheD700Task()
    {
        _db.PfaAccountingEngagements.Add(new PfaAccountingEngagement { Id = Guid.NewGuid(), PfaRegistrationId = _pfa, StartDate = new DateOnly(2026, 1, 1), Status = EngagementStatus.Active });
        _db.TaxRules.AddRange(Application.Accounting.Tax.TaxRuleSeed.Rules);
        await _db.SaveChangesAsync();
        var files = new DeclarationFiles(_db, new AnafDeclarationXmlService(), _files, new PlainSecrets());
        var validator = new DeclarationValidator(_db, new AnafDeclarationXmlService(), _anaf, files, Options.Create(new AccountingOptions()));

        JobRef job = (await new StartMonthJobCommandHandler(_db, new FixedUser(_accountant))
            .Handle(new StartMonthJobCommand(BackgroundJobType.ProcessPeriod, "2026-09", _pfa), CancellationToken.None)).Value;
        (await new RunMonthJobCommandHandler(_db, new NoExtraction(), files, validator, Service()).Handle(new RunMonthJobCommand(job.JobId), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        (await _db.PfaMonthChecks.SingleAsync()).ReasonsJson.ShouldContain("art. 317");
        (await _db.VatRegistrationRequests.SingleAsync(r => r.PfaRegistrationId == _pfa)).Status.ShouldBe(VatRegistrationStatus.Generated);
    }

    [Fact]
    public async Task A_pfa_without_a_request_gets_null_not_an_error()
    {
        Result<VatRegistrationDto?> none = await new GetPfaVatRegistrationQueryHandler(_db).Handle(new GetPfaVatRegistrationQuery(_pfa), CancellationToken.None);
        none.IsSuccess.ShouldBeTrue();
        none.Value.ShouldBeNull();

        await Service().EnsureRequestedAsync(_pfa, null, CancellationToken.None);
        (await new GetPfaVatRegistrationQueryHandler(_db).Handle(new GetPfaVatRegistrationQuery(_pfa), CancellationToken.None)).Value!.Status.ShouldBe(VatRegistrationStatus.Generated);
    }

    private sealed class NoExtraction : Application.Abstractions.Messaging.ICommandHandler<Application.Accounting.Documents.RunPlatformDocumentExtractionCommand>
    {
        public Task<Result> Handle(Application.Accounting.Documents.RunPlatformDocumentExtractionCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    private VatRegistrationService Service()
    {
        var files = new DeclarationFiles(_db, new AnafDeclarationXmlService(), _files, new PlainSecrets());
        var settings = new UpdatePfaSettingsCommandHandler(_db, new FixedUser(_accountant), Options.Create(new AccountingOptions()));
        return new VatRegistrationService(_db, files, new D700Xml(), _anaf, settings);
    }

    private string Xml(VatRegistrationRequest request)
    {
        Document document = _db.Documents.Single(d => d.Id == request.XmlDocumentId);
        return Encoding.UTF8.GetString(_files.Files[document.EncryptedFilePath]);
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

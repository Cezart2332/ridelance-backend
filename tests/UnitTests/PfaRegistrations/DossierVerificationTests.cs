using Application.PfaRegistrations.Onboarding;
using Domain.Documents;
using Domain.PfaRegistrations;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.PfaRegistrations;

/// <summary>
/// Dosarele (ARR, copie conformă) se generează doar după ce echipa a validat actele care intră în ele.
///
/// Validarea e o decizie pe dosar („Validează documentele pentru dosar”), nu statusul actelor: actele
/// pot intra deja „verificate” (aprobarea automată din mediul de test, buletinul validat la pasul 1),
/// iar dosarul se genera fără ca cineva să-l fi văzut.
/// </summary>
public sealed class DossierVerificationTests
{
    private static readonly Guid Client = Guid.NewGuid();

    private static readonly OnboardingSectionCatalog.DocumentRequirement[] Requirements =
    [
        new("Cazier judiciar", [DocumentCategory.CazierJudiciar]),
        new("Aviz medical", [DocumentCategory.AdeverintaMedicala]),
        new("Aviz psihologic", [DocumentCategory.AvizPsihologic]),
    ];

    [Fact]
    public async Task Acte_verificate_dar_nevalidate_pentru_dosar_tot_blocheaza()
    {
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Verified),
            (DocumentCategory.AdeverintaMedicala, DocumentStatus.Verified),
            (DocumentCategory.AvizPsihologic, DocumentStatus.Verified));

        (await DossierAttachments.PendingAsync(db, Client, Requirements, validatedAtUtc: null, CancellationToken.None))
            .ShouldBe(["Cazier judiciar", "Aviz medical", "Aviz psihologic"]);
    }

    [Fact]
    public async Task Dupa_validarea_dosarului_se_poate_genera()
    {
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Pending),
            (DocumentCategory.AdeverintaMedicala, DocumentStatus.Verified),
            (DocumentCategory.AvizPsihologic, DocumentStatus.Pending));

        (await DossierAttachments.PendingAsync(db, Client, Requirements, DateTime.UtcNow.AddMinutes(1), CancellationToken.None))
            .ShouldBeEmpty();
    }

    /// <summary>Un act reîncărcat după validare n-a fost văzut de nimeni: intră din nou la validare.</summary>
    [Fact]
    public async Task Un_act_reincarcat_dupa_validare_asteapta_din_nou()
    {
        DateTime validatedAt = DateTime.UtcNow.AddHours(-1);
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Verified, DateTime.UtcNow.AddDays(-1)),
            (DocumentCategory.CazierJudiciar, DocumentStatus.Verified, DateTime.UtcNow),
            (DocumentCategory.AdeverintaMedicala, DocumentStatus.Verified, DateTime.UtcNow.AddDays(-1)),
            (DocumentCategory.AvizPsihologic, DocumentStatus.Verified, DateTime.UtcNow.AddDays(-1)));

        (await DossierAttachments.ReadinessAsync(db, Client, Requirements, validatedAt, CancellationToken.None))
            .Unverified.ShouldBe(["Cazier judiciar"]);
    }

    [Fact]
    public async Task Un_act_respins_dupa_validare_blocheaza()
    {
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Rejected, DateTime.UtcNow.AddDays(-1)),
            (DocumentCategory.AdeverintaMedicala, DocumentStatus.Verified, DateTime.UtcNow.AddDays(-1)),
            (DocumentCategory.AvizPsihologic, DocumentStatus.Verified, DateTime.UtcNow.AddDays(-1)));

        (await DossierAttachments.ReadinessAsync(db, Client, Requirements, DateTime.UtcNow, CancellationToken.None))
            .Unverified.ShouldBe(["Cazier judiciar"]);
    }

    [Fact]
    public void The_refusal_tells_the_client_what_is_still_in_review()
    {
        Error error = DossierAttachments.NotYetVerified(["Aviz medical", "Cazier judiciar"]);

        error.Description.ShouldContain("Aviz medical");
        error.Description.ShouldContain("Cazier judiciar");
    }

    /// <summary>O piesă lipsă ține și ea dosarul pe loc. Lipsurile vin primele, apoi actele nevalidate.</summary>
    [Fact]
    public async Task Missing_documents_block_the_dossier_along_with_unvalidated_ones()
    {
        using ApplicationDbContext db = await With(
            (DocumentCategory.CazierJudiciar, DocumentStatus.Verified),
            (DocumentCategory.AvizPsihologic, DocumentStatus.Pending));

        (await DossierAttachments.PendingAsync(db, Client, Requirements, validatedAtUtc: null, CancellationToken.None))
            .ShouldBe(["Aviz medical", "Cazier judiciar", "Aviz psihologic"]);
    }

    [Fact]
    public void Dosarul_de_copie_conforma_nu_cere_ce_vine_dupa_depunere()
    {
        // Copia conformă și ecusoanele vin DUPĂ dosar; cât le cerea, dosarul nu se genera niciodată.
        var owned = OnboardingSectionCatalog.RequirementsForVehicleDossier(Domain.PfaRegistrations.VehicleOwnershipMode.Owned)
            .Select(r => r.Label)
            .ToList();
        owned.ShouldNotContain("Copie conformă");
        owned.ShouldNotContain("Ecuson Uber");
        owned.ShouldNotContain("Ecuson Bolt");
        // O mașină proprie n-are contract.
        owned.ShouldNotContain("Contract vehicul");
        owned.ShouldContain("Autorizație transport alternativ");
        owned.ShouldContain("RCA");

        OnboardingSectionCatalog.RequirementsForVehicleDossier(Domain.PfaRegistrations.VehicleOwnershipMode.Rented)
            .Select(r => r.Label)
            .ShouldContain("Contract de închiriere");
    }

    [Fact]
    public async Task Validarea_din_admin_deblocheaza_dosarul_si_marcheaza_actele()
    {
        // Câte un act pe fiecare cerință reală a dosarului ARR, unul încă în verificare.
        IReadOnlyList<OnboardingSectionCatalog.DocumentRequirement> arr =
            OnboardingSectionCatalog.RequirementsFor(OnboardingSectionKey.AutorizatieTransport);
        using ApplicationDbContext db = await With(arr
            .Select((req, i) => (req.AcceptedCategories[0], i == 0 ? DocumentStatus.Pending : DocumentStatus.Verified, DateTime.UtcNow.AddMinutes(-5)))
            .ToArray());
        var registration = new PfaRegistration { Id = Guid.NewGuid(), UserId = Client };
        db.PfaRegistrations.Add(registration);
        await db.SaveChangesAsync();

        (await DossierAttachments.ReadinessAsync(db, Client, arr, validatedAtUtc: null, CancellationToken.None))
            .Unverified.Count.ShouldBe(arr.Count);

        var handler = new ValidateDossierDocumentsCommandHandler(db);
        Result<DossierReadinessResponse> result = await handler.Handle(
            new ValidateDossierDocumentsCommand(registration.Id, DossierSteps.Arr, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        registration.ArrDossierDocumentsValidatedAtUtc.ShouldNotBeNull();
        registration.VehicleDossierDocumentsValidatedAtUtc.ShouldBeNull();
        (await DossierAttachments.ReadinessAsync(
            db, Client, arr, registration.ArrDossierDocumentsValidatedAtUtc, CancellationToken.None)).Ready.ShouldBeTrue();
        (await db.Documents.CountAsync(d => d.Status != DocumentStatus.Verified)).ShouldBe(0);
        (await db.Notifications.CountAsync(n => n.UserId == Client)).ShouldBe(1);
    }

    [Fact]
    public async Task Validarea_din_admin_refuza_cat_lipsesc_acte()
    {
        using ApplicationDbContext db = await With((DocumentCategory.CazierJudiciar, DocumentStatus.Verified));
        var registration = new PfaRegistration { Id = Guid.NewGuid(), UserId = Client };
        db.PfaRegistrations.Add(registration);
        await db.SaveChangesAsync();

        Result<DossierReadinessResponse> result = await new ValidateDossierDocumentsCommandHandler(db).Handle(
            new ValidateDossierDocumentsCommand(registration.Id, DossierSteps.Arr, Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        registration.ArrDossierDocumentsValidatedAtUtc.ShouldBeNull();
    }

    private static Task<ApplicationDbContext> With(params (DocumentCategory Category, DocumentStatus Status)[] documents) =>
        With(documents.Select(d => (d.Category, d.Status, DateTime.UtcNow)).ToArray());

    private static async Task<ApplicationDbContext> With(
        params (DocumentCategory Category, DocumentStatus Status, DateTime UploadedAtUtc)[] documents)
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new Events());

        foreach ((DocumentCategory category, DocumentStatus status, DateTime uploadedAtUtc) in documents)
        {
            db.Documents.Add(new Document
            {
                Id = Guid.NewGuid(),
                UserId = Client,
                Category = category,
                Status = status,
                UploadedAtUtc = uploadedAtUtc,
            });
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return db;
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.FiscalEstimates;
using Application.FiscalProfiles;
using Domain.FiscalEstimates;
using Domain.FiscalProfiles;
using Domain.PfaRegistrations;
using Domain.Users;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.FiscalEstimates;

/// <summary>Coloanele fiscale din „Clienți PFA”: ultima rulare a fiecărui PFA, cu profilul lui.</summary>
public sealed class FiscalOverviewTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new Events());
    private readonly User _staff = new() { Id = Guid.NewGuid(), Role = UserRole.Admin };

    public FiscalOverviewTests() => _db.Users.Add(_staff);

    [Fact]
    public async Task Latest_run_gives_income_and_taxes_and_the_profile_label()
    {
        var pfa = Guid.NewGuid();
        AddProfile(pfa, PfaTaxProfileStatus.Completed, """{"pensioner":"yes","employment":"none"}""");
        AddRun(pfa, new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), income: 5_000, cas: 1);
        AddRun(pfa, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), income: 10_000, cas: 0, stale: true);
        await _db.SaveChangesAsync();

        FiscalOverviewRow row = (await List()).Single();

        row.ProfileStatus.ShouldBe("COMPLETED");
        row.ProfileLabel.ShouldBe("Pensionar");
        row.Stale.ShouldBeTrue();
        // Lunile până la data rulării (septembrie): 9 × 10.000 venit, 9 × 2.000 cheltuieli.
        row.GrossIncome.ShouldBe(90_000);
        row.Expenses.ShouldBe(18_000);
        row.NetIncome.ShouldBe(72_000);
        row.Cas.ShouldBe(0);
        row.Cass.ShouldBe(7_200);
        row.IncomeTax.ShouldBe(6_480);
        row.TotalTaxes.ShouldBe(13_680);
    }

    [Fact]
    public async Task Profile_without_a_run_has_no_figures()
    {
        AddProfile(Guid.NewGuid(), PfaTaxProfileStatus.Draft, "{}");
        await _db.SaveChangesAsync();

        FiscalOverviewRow row = (await List()).Single();

        row.ProfileStatus.ShouldBe("DRAFT");
        row.ProfileLabel.ShouldBeNull();
        row.GrossIncome.ShouldBeNull();
        row.TotalTaxes.ShouldBeNull();
    }

    [Fact]
    public async Task Thresholds_come_from_the_year_parameters()
    {
        FiscalOverviewDto overview = (await new ListFiscalOverviewQueryHandler(_db, new TaxYearParametersProvider(), new FixedUser(_staff.Id))
            .Handle(new ListFiscalOverviewQuery(2026), CancellationToken.None)).Value;

        overview.Thresholds.ShouldBe(new FiscalThresholds(48_600, 97_200, 24_300, 291_600, 395_000));
    }

    [Theory]
    [InlineData("""{"employment":"full","salaryAboveCassMin":"yes","pensioner":"no"}""", "Salariat ≥ 6 salarii")]
    [InlineData("""{"employment":"part","salaryAboveCassMin":"no","pensioner":"no"}""", "Salariat < 6 salarii")]
    [InlineData("""{"employment":"none","student":"yes","pensioner":"no"}""", "Student")]
    [InlineData("""{"employment":"none","ownPensionSystem":"yes"}""", "Sistem propriu")]
    [InlineData("""{"employment":"none","pensioner":"no","student":"no"}""", "Standard")]
    [InlineData("nu e json", "Standard")]
    public void Profile_label_names_the_situation_that_changes_the_taxes(string answers, string label) =>
        FiscalProfileLabels.Of(answers).ShouldBe(label);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Accountant_sees_only_assigned_clients_including_runs_without_profiles()
    {
        _staff.Role = UserRole.Contabil;
        var assigned = Guid.NewGuid();
        var other = Guid.NewGuid();
        var runOnly = Guid.NewGuid();
        AddProfile(assigned, PfaTaxProfileStatus.Completed, "{}");
        AddProfile(other, PfaTaxProfileStatus.Completed, "{}");
        _db.PfaRegistrations.Local.Single(p => p.Id == other).AssignedContabilId = Guid.NewGuid();
        AddPfa(runOnly, Guid.NewGuid());
        AddRun(runOnly, DateTime.UtcNow, 1000, 0);
        await _db.SaveChangesAsync();

        (await List()).Select(row => row.PfaId).ShouldBe([assigned]);
    }

    [Fact]
    public async Task Admin_sees_all_open_accounts_but_excludes_closed_accounts()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var closed = Guid.NewGuid();
        AddProfile(first, PfaTaxProfileStatus.Draft, "{}");
        AddProfile(second, PfaTaxProfileStatus.Completed, "{}");
        AddProfile(closed, PfaTaxProfileStatus.Completed, "{}");
        _db.PfaRegistrations.Local.Single(p => p.Id == second).AssignedContabilId = Guid.NewGuid();
        _db.PfaRegistrations.Local.Single(p => p.Id == closed).User.DeletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        (await List()).Select(row => row.PfaId).ShouldBe([first, second], ignoreOrder: true);
    }

    private async Task<IReadOnlyList<FiscalOverviewRow>> List() =>
        (await new ListFiscalOverviewQueryHandler(_db, new TaxYearParametersProvider(), new FixedUser(_staff.Id)).Handle(new ListFiscalOverviewQuery(2026), CancellationToken.None)).Value.Rows;

    private void AddProfile(Guid pfa, PfaTaxProfileStatus status, string answers)
    {
        AddPfa(pfa, _staff.Id);
        _db.PfaTaxProfiles.Add(new PfaTaxProfile { Id = Guid.NewGuid(), PfaRegistrationId = pfa, TaxYear = 2026, Status = status, AnswersJson = answers });
    }

    private void AddPfa(Guid id, Guid accountant)
    {
        var owner = new User { Id = Guid.NewGuid(), Email = $"{id}@example.test" };
        _db.Users.Add(owner);
        _db.PfaRegistrations.Add(new PfaRegistration { Id = id, UserId = owner.Id, User = owner, AssignedContabilId = accountant });
    }

    private sealed class FixedUser(Guid id) : IUserContext
    {
        public Guid UserId => id;
    }

    private void AddRun(Guid pfa, DateTime createdAt, decimal income, decimal cas, bool stale = false)
    {
        var snapshot = new FinancialSnapshot(
            Guid.NewGuid(), new DateOnly(2026, 9, 30), 2026, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1),
            [.. Enumerable.Range(1, 12).Select(month => new MonthFigures(month, income, 2_000))], 0);
        var run = new FiscalEstimateRun
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfa,
            TaxYear = 2026,
            AsOf = snapshot.AsOf,
            SnapshotJson = JsonSerializer.Serialize(snapshot, FiscalProfileService.Json),
            Status = TaxStatuses.Estimated,
            Stale = stale,
            CreatedAtUtc = createdAt,
        };
        run.Calculations.Add(new FiscalCalculation { Id = Guid.NewGuid(), RunId = run.Id, Component = TaxComponents.Cas, Status = TaxStatuses.Estimated, Amount = cas });
        run.Calculations.Add(new FiscalCalculation { Id = Guid.NewGuid(), RunId = run.Id, Component = TaxComponents.Cass, Status = TaxStatuses.Estimated, Amount = 7_200 });
        run.Calculations.Add(new FiscalCalculation { Id = Guid.NewGuid(), RunId = run.Id, Component = TaxComponents.IncomeTax, Status = TaxStatuses.Estimated, Amount = 6_480 });
        _db.FiscalEstimateRuns.Add(run);
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

using Application.Accounting;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Application.Accounting.Months;
using Domain.Accounting;
using Infrastructure.Database;
using Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shouldly;
using Xunit;

namespace UnitTests.Accounting;

/// <summary>
/// Interogările modulului, executate pe Postgres: EF InMemory evaluează totul în memorie și ar
/// ascunde un LINQ pe care Npgsql nu-l poate traduce. Rulează doar cu
/// <c>RIDELANCE_TEST_DATABASE</c> setat (o bază migrată, de test); altfel nu face nimic.
/// </summary>
public sealed class PostgresQueryTranslationTests
{
    private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RIDELANCE_TEST_DATABASE");

    [Fact]
    public async Task Document_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());

        var document = new PlatformDocument
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = Guid.NewGuid(),
            Period = "2026-08",
            Platform = Platform.Bolt,
            DocumentType = PlatformDocumentType.CommissionInvoice,
            Status = PlatformDocumentStatus.Confirmed,
            PdfText = "Comision: 1.000,00",
        };
        var fields = new ExtractedFields(
            "Bolt Operations OÜ", "EE", "EE102090374", "X-1", new DateOnly(2026, 8, 31), null, null, "RON", 1000m, 1000m, []);

        IReadOnlyList<DocumentCheck> checks = await PlatformDocumentSupport.RunChecksAsync(db, document, fields, new AccountingOptions(), CancellationToken.None);
        checks.ShouldNotBeEmpty();

        (await PlatformDocumentSupport.LockReasonAsync(db, document, CancellationToken.None)).ShouldBeNull();
        (await PlatformDocumentSupport.IncludedInAsync(db, document.Id, CancellationToken.None)).ShouldBeEmpty();
        (await PlatformDocumentSupport.EnsureWritableAsync(db, document.PfaRegistrationId, document.Period, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await PlatformDocumentSupport.CurrentExtractionAsync(db, document.Id, CancellationToken.None)).ShouldBeNull();

        Result<IReadOnlyList<PlatformDocumentListItem>> list = await new ListPlatformDocumentsQueryHandler(db)
            .Handle(new ListPlatformDocumentsQuery(document.PfaRegistrationId, document.Period), CancellationToken.None);
        list.Value.ShouldBeEmpty();

        Result<PlatformDocumentDetail> missing = await new GetPlatformDocumentQueryHandler(db, Options.Create(new AccountingOptions()))
            .Handle(new GetPlatformDocumentQuery(document.Id), CancellationToken.None);
        missing.Error.ShouldBe(AccountingErrors.DocumentNotFound);
    }

    [Fact]
    public async Task Month_queries_translate_to_sql()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new Events());

        List<ScopePfa> scope = await AccountingScope.InPeriodAsync(db, "2026-08", CancellationToken.None);
        MonthData data = await MonthData.LoadAsync(db, "2026-08", [.. scope.Select(p => p.Id), Guid.NewGuid()], CancellationToken.None);
        data.Suppliers.ShouldNotBeEmpty();
        (await DeclarationSummaries.CurrentVersionsAsync(db, "2026-08", [Guid.NewGuid()], CancellationToken.None)).ShouldBeEmpty();

        Result<PeriodOverview> overview = await new GetPeriodOverviewQueryHandler(db, Options.Create(new AccountingOptions()))
            .Handle(new GetPeriodOverviewQuery("2026-08"), CancellationToken.None);
        overview.IsSuccess.ShouldBeTrue();
        (await PreCheck.RunAsync(db, Guid.NewGuid(), "2026-08", new AccountingOptions(), CancellationToken.None)).ShouldBeNull();
    }

    private sealed class Events : IDomainEventsDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

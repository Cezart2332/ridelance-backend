using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Rentals;
using Domain.Cars;
using Domain.Documents;
using Domain.Maintenance;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.SrlDashboard.Queries.GetSrlHome;

/// <summary>Pagina Acasă a dashboardului SRL: cifrele flotei și ce necesită atenție.</summary>
public sealed record GetSrlHomeQuery : IQuery<SrlHomeDto>;

internal sealed class GetSrlHomeQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetSrlHomeQuery, SrlHomeDto>
{
    private const decimal WeeksPerMonth = 52m / 12m;

    /// <summary>Ferestrele în care un termen devine „curând". Aceleași pentru cifre și pentru alerte.</summary>
    private const int DocumentHorizonDays = 30;
    private const int MaintenanceHorizonDays = 14;

    /// <summary>Câte lucruri se arată în „Necesită atenție". O listă lungă nu mai e o listă de priorități.</summary>
    private const int MaxAttentionItems = 6;

    public async Task<Result<SrlHomeDto>> Handle(GetSrlHomeQuery query, CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;
        DateTime now = DateTime.UtcNow;

        List<Car> cars = await context.Cars
            .AsNoTracking()
            .Include(c => c.Images)
            .Where(c => c.PostedByUserId == userId)
            .ToListAsync(cancellationToken);

        List<Rental> rentals = await context.Rentals
            .AsNoTracking()
            .Include(r => r.Tenant)
            .Where(r => r.OwnerUserId == userId)
            .ToListAsync(cancellationToken);

        List<MaintenanceEntry> maintenance = await context.MaintenanceEntries
            .AsNoTracking()
            .Where(m => m.OwnerUserId == userId)
            .ToListAsync(cancellationToken);

        List<Document> expiringDocuments = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId
                && d.ExpiresAtUtc != null
                && d.ExpiresAtUtc > now
                && d.ExpiresAtUtc <= now.AddDays(DocumentHorizonDays))
            .OrderBy(d => d.ExpiresAtUtc)
            .ToListAsync(cancellationToken);

        var carLabels = cars.ToDictionary(
            c => c.Id,
            c => string.Create(CultureInfo.InvariantCulture, $"{c.Brand} {c.Model}, {c.Year}"));

        // Deschise = în curs sau aproape de predare. O rezervare viitoare nu ocupă mașina azi.
        var open = rentals
            .Where(r => RentalStatus.For(r, now) is RentalStatus.Active or RentalStatus.EndingSoon)
            .ToList();

        var rentedCarIds = open.Select(r => r.CarId).ToHashSet();

        var scheduled = maintenance
            .Where(m => m.PerformedAtUtc > now && m.PerformedAtUtc <= now.AddDays(MaintenanceHorizonDays))
            .ToList();

        var attention = new List<AttentionItemDto>();

        foreach (Document document in expiringDocuments)
        {
            int days = (int)Math.Ceiling((document.ExpiresAtUtc!.Value - now).TotalDays);
            attention.Add(new AttentionItemDto(
                $"doc-{document.Id}",
                days <= 7 ? "danger" : "warning",
                $"{document.Category} expiră în {days} {(days == 1 ? "zi" : "zile")}",
                document.OriginalFileName,
                "documente-societate"));
        }

        foreach (Rental rental in open.Where(r => RentalStatus.For(r, now) == RentalStatus.EndingSoon))
        {
            int days = (int)Math.Ceiling((rental.EndAtUtc - now).TotalDays);
            attention.Add(new AttentionItemDto(
                $"rental-{rental.Id}",
                "warning",
                $"Predare în {days} {(days == 1 ? "zi" : "zile")}",
                $"{carLabels.GetValueOrDefault(rental.CarId, "Mașină")} · {rental.Tenant.Name}",
                "inchirieri"));
        }

        foreach (MaintenanceEntry entry in scheduled)
        {
            attention.Add(new AttentionItemDto(
                $"maint-{entry.Id}",
                "info",
                entry.Title,
                $"{carLabels.GetValueOrDefault(entry.CarId, "Mașină")} · {entry.PerformedAtUtc:dd.MM.yyyy}",
                "mentenanta"));
        }

        // Anunțurile slabe intră ultimele: sunt o oportunitate, nu o urgență.
        foreach (Car car in cars.Where(c => c.Active && c.RecommendationScore < 50).OrderBy(c => c.RecommendationScore))
        {
            attention.Add(new AttentionItemDto(
                $"score-{car.Id}",
                "info",
                $"Anunț slab poziționat: {car.RecommendationScore}/100",
                $"{carLabels.GetValueOrDefault(car.Id, "Mașină")} · completează-l ca să apară mai sus",
                "masini"));
        }

        var rows = open
            .OrderBy(r => r.EndAtUtc)
            .Select(r => new ActiveRentalRowDto(
                r.Id,
                carLabels.GetValueOrDefault(r.CarId, "Mașină ștearsă"),
                r.Tenant.Name,
                r.StartAtUtc,
                r.EndAtUtc,
                r.WeeklyRentBani,
                RentalStatus.For(r, now)))
            .ToList();

        return Result.Success(new SrlHomeDto(
            cars.Count,
            cars.Count(c => c.Active && c.ApprovalStatus == CarApprovalStatus.Approved),
            rentedCarIds.Count,
            Math.Max(0, cars.Count - rentedCarIds.Count),
            open.Count,
            (long)open.Sum(r => r.WeeklyRentBani * WeeksPerMonth),
            expiringDocuments.Count,
            scheduled.Count,
            attention.Take(MaxAttentionItems).ToList(),
            rows));
    }
}

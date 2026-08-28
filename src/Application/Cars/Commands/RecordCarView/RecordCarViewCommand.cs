using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Application.Cars;

namespace Application.Cars.Commands.RecordCarView;

/// <summary>
/// O vizualizare a paginii de detaliu. <paramref name="VisitorHash"/> se calculează în endpoint,
/// din datele cererii — stratul de aplicație nu vede niciodată IP-ul.
/// </summary>
public sealed record RecordCarViewCommand(Guid CarId, string VisitorHash, string Source) : ICommand;

internal sealed class RecordCarViewCommandHandler(IApplicationDbContext context)
    : ICommandHandler<RecordCarViewCommand>
{
    /// <summary>Cât timp același vizitator nu mai contorizează pentru aceeași mașină.</summary>
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(30);

    public async Task<Result> Handle(RecordCarViewCommand command, CancellationToken cancellationToken)
    {
        bool carExists = await context.Cars
            .AsNoTracking()
            .Where(c => c.Id == command.CarId)
            .Where(CarVisibility.IsPublic)
            .AnyAsync(cancellationToken);

        if (!carExists)
        {
            return Result.Failure(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        DateTime now = DateTime.UtcNow;

        // Cine a mai fost aici recent nu mai numără: refresh, back/forward și al doilea tab sunt
        // aceeași vizită, indiferent ce crede clientul.
        bool seenRecently = await context.CarViews
            .AsNoTracking()
            .AnyAsync(
                v => v.CarId == command.CarId &&
                     v.VisitorHash == command.VisitorHash &&
                     v.CreatedAtUtc > now - DedupWindow,
                cancellationToken);

        if (seenRecently)
        {
            return Result.Success();
        }

        bool seenBefore = await context.CarViews
            .AsNoTracking()
            .AnyAsync(
                v => v.CarId == command.CarId && v.VisitorHash == command.VisitorHash,
                cancellationToken);

        context.CarViews.Add(new CarView
        {
            Id = Guid.NewGuid(),
            CarId = command.CarId,
            VisitorHash = command.VisitorHash,
            Source = TrafficSource.Normalize(command.Source),
            CreatedAtUtc = now,
        });

        await context.SaveChangesAsync(cancellationToken);

        // Contoarele denormalizate rămân pentru ecranele care afișează un total: rândurile de mai
        // sus răspund la „ultimele 7 zile”, dar un `count(*)` pe toată istoria e risipă.
        await context.Cars
            .Where(c => c.Id == command.CarId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(c => c.ViewCount, c => c.ViewCount + 1)
                    .SetProperty(c => c.UniqueViewCount, c => seenBefore ? c.UniqueViewCount : c.UniqueViewCount + 1),
                cancellationToken);

        return Result.Success();
    }
}

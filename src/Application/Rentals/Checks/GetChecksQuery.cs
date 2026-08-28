using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Checks;

/// <summary>Predarea și primirea unei închirieri, împreună: se citesc ca să se compare.</summary>
public sealed record GetChecksQuery(Guid RentalId) : IQuery<ChecksDto>;

public sealed record ChecksDto(CheckRecordDto? CheckIn, CheckRecordDto? CheckOut);

public sealed record CheckRecordDto(
    Guid Id,
    string Kind,
    DateTime OccurredAtUtc,
    int Mileage,
    string? FuelLevel,
    IReadOnlyList<string> Accessories,
    string? Notes,
    long? DepositReturnedBani,
    long? DepositWithheldBani,
    string? WithholdingReason,
    long? ExtraMileageChargeBani,
    long? OtherChargesBani,
    IReadOnlyList<CheckPhotoDto> Photos);

public sealed record CheckPhotoDto(Guid Id, string Slot, Guid DocumentId);

internal sealed class GetChecksQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetChecksQuery, ChecksDto>
{
    public async Task<Result<ChecksDto>> Handle(GetChecksQuery query, CancellationToken cancellationToken)
    {
        bool owns = await context.Rentals
            .AsNoTracking()
            .AnyAsync(r => r.Id == query.RentalId && r.OwnerUserId == userContext.UserId, cancellationToken);

        if (!owns)
        {
            return Result.Failure<ChecksDto>(Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        List<CheckRecord> records = await context.CheckRecords
            .AsNoTracking()
            .Include(c => c.Photos)
            .Where(c => c.RentalId == query.RentalId)
            .ToListAsync(cancellationToken);

        return Result.Success(new ChecksDto(
            Map(records.Find(c => c.Kind == CheckKind.CheckIn)),
            Map(records.Find(c => c.Kind == CheckKind.CheckOut))));
    }

    private static CheckRecordDto? Map(CheckRecord? record) => record is null
        ? null
        : new CheckRecordDto(
            record.Id,
            record.Kind.ToString(),
            record.OccurredAtUtc,
            record.Mileage,
            record.FuelLevel,
            record.Accessories,
            record.Notes,
            record.DepositReturnedBani,
            record.DepositWithheldBani,
            record.WithholdingReason,
            record.ExtraMileageChargeBani,
            record.OtherChargesBani,
            record.Photos
                .OrderBy(p => p.Slot)
                .Select(p => new CheckPhotoDto(p.Id, p.Slot.ToString(), p.DocumentId))
                .ToList());
}

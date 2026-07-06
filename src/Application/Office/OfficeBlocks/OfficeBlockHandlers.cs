using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Office;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Office.OfficeBlocks;

/// <summary>Blocks one 30-minute slot, or the whole day when Time is null.</summary>
public sealed record BlockOfficeSlotCommand(string Date, string? Time, string? Note) : ICommand<Guid>;

internal sealed class BlockOfficeSlotCommandHandler(IApplicationDbContext context)
    : ICommandHandler<BlockOfficeSlotCommand, Guid>
{
    public async Task<Result<Guid>> Handle(BlockOfficeSlotCommand command, CancellationToken cancellationToken)
    {
        if (!OfficeCalendar.TryParseDate(command.Date, out DateOnly date))
        {
            return Result.Failure<Guid>(Error.Problem("Office.InvalidDate", "Data este invalidă."));
        }

        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(command.Time))
        {
            if (!OfficeCalendar.TryParseTime(command.Time, out TimeOnly parsed))
            {
                return Result.Failure<Guid>(Error.Problem("Office.InvalidTime", "Ora este invalidă."));
            }

            time = parsed;
        }

        bool exists = await context.OfficeBlockedSlots
            .AnyAsync(b => b.Date == date && b.StartTime == time, cancellationToken);
        if (exists)
        {
            return Result.Failure<Guid>(Error.Conflict("Office.AlreadyBlocked", "Intervalul este deja blocat."));
        }

        var block = new OfficeBlockedSlot
        {
            Id = Guid.NewGuid(),
            Date = date,
            StartTime = time,
            Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };

        context.OfficeBlockedSlots.Add(block);
        await context.SaveChangesAsync(cancellationToken);

        return block.Id;
    }
}

/// <summary>Removes a block (frees the slot or the day).</summary>
public sealed record UnblockOfficeSlotCommand(Guid BlockId) : ICommand;

internal sealed class UnblockOfficeSlotCommandHandler(IApplicationDbContext context)
    : ICommandHandler<UnblockOfficeSlotCommand>
{
    public async Task<Result> Handle(UnblockOfficeSlotCommand command, CancellationToken cancellationToken)
    {
        OfficeBlockedSlot? block = await context.OfficeBlockedSlots
            .FirstOrDefaultAsync(b => b.Id == command.BlockId, cancellationToken);

        if (block is null)
        {
            return Result.Failure(Error.NotFound("Office.BlockNotFound", "Blocarea nu a fost găsită."));
        }

        context.OfficeBlockedSlots.Remove(block);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

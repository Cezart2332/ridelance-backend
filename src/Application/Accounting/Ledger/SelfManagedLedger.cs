using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>
/// Registrele PFAlone, completate de titular. Aceeași înregistrare manuală ca a contabilului
/// (<see cref="CreateManualLedgerEntryCommand"/>), cu motivul pus de noi: titularul își trece
/// propriile încasări și plăți, nu corectează ce a importat altcineva.
/// </summary>
public sealed record AddOwnLedgerEntryCommand(Guid PfaId, ManualLedgerEntryRequest Request) : ICommand<LedgerEntryDto>;

/// <summary>Șterge o înregistrare trecută de titular, cât timp luna ei e deschisă.</summary>
public sealed record DeleteOwnLedgerEntryCommand(Guid PfaId, Guid EntryId) : ICommand;

internal sealed class AddOwnLedgerEntryCommandHandler(ICommandHandler<CreateManualLedgerEntryCommand, LedgerEntryDto> manual)
    : ICommandHandler<AddOwnLedgerEntryCommand, LedgerEntryDto>
{
    private const string OwnEntryReason = "Înregistrare trecută de titular (PFAlone).";

    public Task<Result<LedgerEntryDto>> Handle(AddOwnLedgerEntryCommand command, CancellationToken cancellationToken) =>
        manual.Handle(
            new CreateManualLedgerEntryCommand(command.PfaId, command.Request with { Reason = OwnEntryReason }),
            cancellationToken);
}

internal sealed class DeleteOwnLedgerEntryCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<DeleteOwnLedgerEntryCommand>
{
    public async Task<Result> Handle(DeleteOwnLedgerEntryCommand command, CancellationToken cancellationToken)
    {
        LedgerEntry? entry = await db.LedgerEntries
            .FirstOrDefaultAsync(e => e.Id == command.EntryId && e.PfaRegistrationId == command.PfaId, cancellationToken);

        // Doar ce a trecut el: un rând importat sau corectat de contabil nu e al lui de șters.
        if (entry is null || entry.Source != LedgerSource.Manual)
        {
            return Result.Failure(LedgerErrors.EntryNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, LedgerSupport.PeriodOf(entry.Date), cancellationToken);
        if (writable.IsFailure)
        {
            return writable;
        }

        AccountingAudit.Record(
            db, command.PfaId, nameof(LedgerEntry), entry.Id, "DELETE_OWN", new { entry.Date, entry.Description, entry.Amount }, null,
            "Ștearsă de titular (PFAlone).", userContext.UserId);
        db.LedgerEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

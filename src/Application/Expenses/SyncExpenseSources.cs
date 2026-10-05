using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Anaf;
using Application.Accounting.Ledger;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Expenses;

public sealed record SyncExpenseSourcesCommand(Guid PfaId) : ICommand<IReadOnlyList<string>>;

internal sealed class SyncExpenseSourcesCommandHandler(IApplicationDbContext db, IUserContext user,
    ICommandHandler<SyncPfaEFacturaCommand, EFacturaSyncResult> invoices,
    ICommandHandler<RunLedgerImportCommand, IReadOnlyList<LedgerImportResult>> ledger)
    : ICommandHandler<SyncExpenseSourcesCommand, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(SyncExpenseSourcesCommand command, CancellationToken cancellationToken)
    {
        Domain.PfaRegistrations.PfaRegistration? pfa = await db.PfaRegistrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.PfaId, cancellationToken);
        User? caller = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == user.UserId, cancellationToken);
        if (pfa is null)
        {
            return Result.Failure<IReadOnlyList<string>>(ExpenseErrors.PfaNotFound);
        }
        if (caller is null || caller.IsDeleted || !(caller.Role == UserRole.Admin ||
            caller.Role == UserRole.Contabil && pfa.AssignedContabilId == caller.Id ||
            caller.Role == UserRole.Client && pfa.UserId == caller.Id))
        {
            return Result.Failure<IReadOnlyList<string>>(ExpenseErrors.AccessDenied);
        }
        var notes = new List<string>();
        Result<EFacturaSyncResult> synced = await invoices.Handle(new SyncPfaEFacturaCommand(pfa.Id), cancellationToken);
        if (synced.IsFailure)
        {
            notes.Add($"Facturi ANAF: {synced.Error.Description}");
        }
        else
        {
            notes.Add($"ANAF: {synced.Value.NewMessages} mesaje noi, {synced.Value.Downloaded} documente descărcate.");
        }
        Result<IReadOnlyList<LedgerImportResult>> imported = await ledger.Handle(new RunLedgerImportCommand(pfa.Id), cancellationToken);
        if (imported.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(imported.Error);
        }
        notes.AddRange(imported.Value.SelectMany(r => r.Notes));
        notes.Add("Plățile bancare disponibile au fost actualizate; facturile se asociază plăților existente, fără a le retransmite la ANAF.");
        return notes;
    }
}

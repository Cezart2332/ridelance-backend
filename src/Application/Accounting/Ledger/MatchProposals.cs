using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>Tranzacția bancară dintr-o propunere, cum o vede omul.</summary>
public sealed record MatchProposalTransactionDto(Guid Id, DateOnly? Date, decimal Amount, string? Counterparty, string? Details);

/// <summary>„Am găsit plata acestui bon în cont. Asociază?” (spec flux contabil R36).</summary>
public sealed record MatchProposalDto(Guid Id, MatchProposalTransactionDto Transaction, LedgerEntryDto Entry, DateTime CreatedAtUtc);

/// <summary><c>GET /accounting/pfas/{pfaId}/match-proposals</c> — propunerile în așteptare.</summary>
public sealed record ListMatchProposalsQuery(Guid PfaId) : IQuery<IReadOnlyList<MatchProposalDto>>;

/// <summary><c>POST /accounting/match-proposals/{id}/accept</c> sau <c>/reject</c>.</summary>
public sealed record ResolveMatchProposalCommand(Guid ProposalId, bool Accept) : ICommand<LedgerEntryDto?>;

internal static class MatchProposalErrors
{
    public static readonly Error NotFound = Error.NotFound("Accounting.MatchProposalNotFound", "Propunerea nu există.");

    public static readonly Error Resolved = Error.Conflict("Accounting.MatchProposalResolved", "Propunerea are deja un răspuns.");
}

internal sealed class ListMatchProposalsQueryHandler(IApplicationDbContext db) : IQueryHandler<ListMatchProposalsQuery, IReadOnlyList<MatchProposalDto>>
{
    public async Task<Result<IReadOnlyList<MatchProposalDto>>> Handle(ListMatchProposalsQuery query, CancellationToken cancellationToken)
    {
        var pending = await db.LedgerMatchProposals.AsNoTracking()
            .Where(p => p.PfaRegistrationId == query.PfaId && p.Accepted == null)
            .OrderBy(p => p.CreatedAtUtc)
            .Join(db.BankTransactions, p => p.BankTransactionId, t => t.Id, (p, t) => new { Proposal = p, Transaction = t })
            .ToListAsync(cancellationToken);

        List<LedgerEntryDto> entries = await LedgerSupport.DtosAsync(
            db.LedgerEntries.AsNoTracking().Where(e => pending.Select(p => p.Proposal.LedgerEntryId).Contains(e.Id)), cancellationToken);

        return pending
            .Where(p => entries.Any(e => e.Id == p.Proposal.LedgerEntryId))
            .Select(p => new MatchProposalDto(
                p.Proposal.Id,
                new MatchProposalTransactionDto(p.Transaction.Id, p.Transaction.BookingDate ?? p.Transaction.ValueDate, p.Transaction.Amount, p.Transaction.CounterpartyName, p.Transaction.RemittanceInfo),
                entries.Single(e => e.Id == p.Proposal.LedgerEntryId),
                p.Proposal.CreatedAtUtc))
            .ToList();
    }
}

/// <summary>
/// Acceptul face din înregistrarea bonului plata bancară: canal <c>BANK</c>, legată de tranzacție, la
/// data extrasului. Rămâne o singură înregistrare. Respingerea lasă tranzacția să devină o plată
/// separată la următorul import.
/// </summary>
internal sealed class ResolveMatchProposalCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<ResolveMatchProposalCommand, LedgerEntryDto?>
{
    public async Task<Result<LedgerEntryDto?>> Handle(ResolveMatchProposalCommand command, CancellationToken cancellationToken)
    {
        LedgerMatchProposal? proposal = await db.LedgerMatchProposals.SingleOrDefaultAsync(p => p.Id == command.ProposalId, cancellationToken);
        if (proposal is null)
        {
            return Result.Failure<LedgerEntryDto?>(MatchProposalErrors.NotFound);
        }

        if (proposal.Accepted is not null)
        {
            return Result.Failure<LedgerEntryDto?>(MatchProposalErrors.Resolved);
        }

        proposal.Accepted = command.Accept;
        proposal.ResolvedAtUtc = DateTime.UtcNow;
        proposal.ResolvedByUserId = userContext.UserId;

        if (!command.Accept)
        {
            AccountingAudit.Record(db, proposal.PfaRegistrationId, nameof(LedgerMatchProposal), proposal.Id, "REJECT", null, null, null, userContext.UserId);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success<LedgerEntryDto?>(null);
        }

        LedgerEntry? entry = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == proposal.LedgerEntryId, cancellationToken);
        BankTransaction? transaction = await db.BankTransactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == proposal.BankTransactionId, cancellationToken);
        DateOnly? booked = transaction?.BookingDate ?? transaction?.ValueDate;
        if (entry is null || transaction is null || booked is not { } date)
        {
            return Result.Failure<LedgerEntryDto?>(LedgerErrors.EntryNotFound);
        }

        Result editable = await UpdateLedgerEntryCommandHandler.EnsureEditableAsync(db, entry, cancellationToken);
        string period = LedgerSupport.PeriodOf(date);
        Result target = editable.IsFailure ? editable : await PlatformDocumentSupport.EnsureWritableAsync(db, entry.PfaRegistrationId, period, cancellationToken);
        if (target.IsFailure)
        {
            return Result.Failure<LedgerEntryDto?>(target.Error);
        }

        var before = new { entry.PaymentMethod, entry.Date, entry.DocumentLabel };
        entry.PaymentMethod = PaymentMethod.Bank;
        entry.BankTransactionId = transaction.Id;
        entry.DocumentDate ??= entry.Date;
        entry.Date = date;
        entry.AccountingPeriod = period;
        entry.DocumentLabel = $"Extras {date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}";

        Result valid = await LedgerSupport.ValidateAsync(db, entry, cancellationToken);
        if (valid.IsFailure)
        {
            return Result.Failure<LedgerEntryDto?>(valid.Error);
        }

        AccountingAudit.Record(
            db, entry.PfaRegistrationId, nameof(LedgerEntry), entry.Id, "MATCH_BANK",
            before, new { entry.PaymentMethod, entry.Date, entry.DocumentLabel, bankTransactionId = transaction.Id }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await LedgerSupport.DtoAsync(db, entry.Id, cancellationToken);
    }
}

using System.Text.Json.Serialization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Ledger;

/// <summary>Ce vede PFA-ul pe un rând (spec flux contabil §8, ecranul Tranzacții).</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<ClientTransactionState>))]
public enum ClientTransactionState
{
    /// <summary>R02: plată fără document — „Asociază bon”.</summary>
    DocumentMissing = 0,

    /// <summary>R04: factura e-Factura găsită automat.</summary>
    InvoiceFound = 1,

    /// <summary>Bon sau document atașat.</summary>
    DocumentAttached = 2,

    /// <summary>La verificare (R12, R23, R33, încasare neidentificată).</summary>
    NeedsReview = 3,

    /// <summary>R20: payout identificat, așteaptă reconcilierea.</summary>
    PayoutPending = 4,

    /// <summary>R21: payout reconciliat.</summary>
    PayoutReconciled = 5,

    /// <summary>R40, R41, R43: transfer cu titularul sau între conturile PFA.</summary>
    Transfer = 6,

    /// <summary>R42: plată către ANAF / Trezorerie.</summary>
    Tax = 7,

    /// <summary>Încasare: raport Z, factură Oblio.</summary>
    Income = 8,

    /// <summary>§4: stornarea unei înregistrări dintr-o lună închisă, corectată de contabil.</summary>
    Correction = 9,
}

/// <summary>Un rând din Tranzacții. Un payout reconciliat e un singur rând, cu suma virată.</summary>
/// <param name="LedgerEntryId">Înregistrarea căreia i se poate asocia un bon, la <c>DOCUMENT_MISSING</c>.</param>
public sealed record ClientTransactionDto(
    Guid Id,
    DateOnly Date,
    decimal Amount,
    string Title,
    string? Detail,
    PaymentMethod PaymentMethod,
    ClientTransactionState State,
    Guid? LedgerEntryId);

public sealed record ClientTransactionsDto(int AttentionCount, IReadOnlyList<ClientTransactionDto> Rows, IReadOnlyList<MatchProposalDto> Proposals);

/// <summary><c>GET /pfa/ledger/transactions?from&amp;to</c> — tranzacțiile PFA-ului utilizatorului.</summary>
public sealed record GetClientTransactionsQuery(DateOnly From, DateOnly To) : IQuery<ClientTransactionsDto>;

/// <summary><c>POST /pfa/ledger/expense-documents</c></summary>
public sealed record UploadClientExpenseDocumentCommand(LedgerUpload File) : ICommand<ExpenseDocumentUploadResult>;

/// <summary><c>POST /pfa/ledger/expense-documents/{id}/confirm</c></summary>
public sealed record ConfirmClientExpenseDocumentCommand(Guid ExpenseDocumentId, ExpensePaymentChoice Payment, Guid? LedgerEntryId, decimal? PersonalAmount)
    : ICommand<LedgerEntryDto>;

/// <summary><c>POST /pfa/ledger/match-proposals/{id}/accept|reject</c></summary>
public sealed record ResolveClientMatchProposalCommand(Guid ProposalId, bool Accept) : ICommand<LedgerEntryDto?>;

internal static class ClientLedger
{
    public static readonly Error NoPfa = Error.NotFound("Accounting.NoPfa", "Contul nu are un PFA înregistrat.");

    /// <summary>PFA-ul utilizatorului logat; un client lucrează doar pe al lui.</summary>
    public static Task<Guid?> PfaIdAsync(IApplicationDbContext db, Guid userId, CancellationToken cancellationToken) =>
        db.PfaRegistrations.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public static bool NeedsAttention(ClientTransactionState state) =>
        state is ClientTransactionState.DocumentMissing or ClientTransactionState.NeedsReview;

    /// <summary>Rândurile: înregistrările grupate pe tranzacția bancară (venit brut + comision = un payout).</summary>
    public static List<ClientTransactionDto> Rows(IEnumerable<LedgerEntry> entries) =>
        [.. entries
            .GroupBy(e => e.SettlementGroupId ?? e.Id)
            .Select(group => Row([.. group]))
            .OrderByDescending(row => row.Date)
            .ThenBy(row => row.Title, StringComparer.Ordinal)];

    private static ClientTransactionDto Row(List<LedgerEntry> group)
    {
        LedgerEntry main = group.OrderByDescending(e => Math.Abs(e.Amount)).First();
        if (group.Count > 1 || main.SettlementGroupId is not null)
        {
            string platform = main.Source == LedgerSource.Bolt ? "Bolt" : "Uber";
            return new ClientTransactionDto(main.Id, main.Date, group.Sum(e => e.Amount), $"Payout {platform}", null, main.PaymentMethod, ClientTransactionState.PayoutReconciled, null);
        }

        ClientTransactionState state = main switch
        {
            { StornoOfEntryId: not null } => ClientTransactionState.Correction,
            { TransactionType: LedgerTransactionType.PlatformSettlement } => ClientTransactionState.PayoutPending,
            { ReconciliationStatus: ReconciliationStatus.NeedsReview } or { Status: LedgerEntryStatus.NeedsReview, ReconciliationStatus: not ReconciliationStatus.Matched } => ClientTransactionState.NeedsReview,
            { TransactionType: LedgerTransactionType.OwnerWithdrawal or LedgerTransactionType.OwnerContribution or LedgerTransactionType.InternalTransfer or LedgerTransactionType.Transfer } => ClientTransactionState.Transfer,
            { TransactionType: LedgerTransactionType.Tax } => ClientTransactionState.Tax,
            { EFacturaMessageId: not null } => ClientTransactionState.InvoiceFound,
            { ReconciliationStatus: ReconciliationStatus.Unmatched } => ClientTransactionState.DocumentMissing,
            { Amount: > 0 } => ClientTransactionState.Income,
            _ => ClientTransactionState.DocumentAttached,
        };

        return new ClientTransactionDto(
            main.Id,
            main.Date,
            main.Amount,
            main.Counterparty ?? main.Description,
            main.Counterparty is null || main.Description.Contains(main.Counterparty, StringComparison.OrdinalIgnoreCase) ? null : main.Description,
            main.PaymentMethod,
            state,
            state == ClientTransactionState.DocumentMissing ? main.Id : null);
    }
}

internal sealed class GetClientTransactionsQueryHandler(IApplicationDbContext db, IUserContext userContext)
    : IQueryHandler<GetClientTransactionsQuery, ClientTransactionsDto>
{
    public async Task<Result<ClientTransactionsDto>> Handle(GetClientTransactionsQuery query, CancellationToken cancellationToken)
    {
        if (await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is not { } pfaId)
        {
            return Result.Failure<ClientTransactionsDto>(ClientLedger.NoPfa);
        }

        List<LedgerEntry> entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId && e.Date >= query.From && e.Date <= query.To)
            .ToListAsync(cancellationToken);
        List<ClientTransactionDto> rows = ClientLedger.Rows(entries);
        IReadOnlyList<MatchProposalDto> proposals = (await new ListMatchProposalsQueryHandler(db).Handle(new ListMatchProposalsQuery(pfaId), cancellationToken)).Value;

        return new ClientTransactionsDto(rows.Count(r => ClientLedger.NeedsAttention(r.State)) + proposals.Count, rows, proposals);
    }
}

internal sealed class UploadClientExpenseDocumentCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    ICommandHandler<UploadExpenseDocumentCommand, ExpenseDocumentUploadResult> upload)
    : ICommandHandler<UploadClientExpenseDocumentCommand, ExpenseDocumentUploadResult>
{
    public async Task<Result<ExpenseDocumentUploadResult>> Handle(UploadClientExpenseDocumentCommand command, CancellationToken cancellationToken) =>
        await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is { } pfaId
            ? await upload.Handle(new UploadExpenseDocumentCommand(pfaId, command.File), cancellationToken)
            : Result.Failure<ExpenseDocumentUploadResult>(ClientLedger.NoPfa);
}

internal sealed class ConfirmClientExpenseDocumentCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    ICommandHandler<ConfirmExpenseDocumentCommand, LedgerEntryDto> confirm)
    : ICommandHandler<ConfirmClientExpenseDocumentCommand, LedgerEntryDto>
{
    public async Task<Result<LedgerEntryDto>> Handle(ConfirmClientExpenseDocumentCommand command, CancellationToken cancellationToken) =>
        await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is { } pfaId
            ? await confirm.Handle(new ConfirmExpenseDocumentCommand(pfaId, command.ExpenseDocumentId, command.Payment, command.LedgerEntryId, command.PersonalAmount, null), cancellationToken)
            : Result.Failure<LedgerEntryDto>(ClientLedger.NoPfa);
}

internal sealed class ResolveClientMatchProposalCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    ICommandHandler<ResolveMatchProposalCommand, LedgerEntryDto?> resolve)
    : ICommandHandler<ResolveClientMatchProposalCommand, LedgerEntryDto?>
{
    public async Task<Result<LedgerEntryDto?>> Handle(ResolveClientMatchProposalCommand command, CancellationToken cancellationToken)
    {
        Guid? pfaId = await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken);
        bool own = pfaId is not null && await db.LedgerMatchProposals.AnyAsync(p => p.Id == command.ProposalId && p.PfaRegistrationId == pfaId, cancellationToken);
        return own
            ? await resolve.Handle(new ResolveMatchProposalCommand(command.ProposalId, command.Accept), cancellationToken)
            : Result.Failure<LedgerEntryDto?>(MatchProposalErrors.NotFound);
    }
}

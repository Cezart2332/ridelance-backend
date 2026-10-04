using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Documents;
using Application.Accounting.Ledger;
using Domain.Accounting;
using Domain.Banking;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Registers;

/// <summary>O clasificare posibilă, cu explicația ei din RJIP.</summary>
public sealed record ClassificationOptionDto(BankClassification Classification, string Label);

/// <summary>Un rând de rezolvat: tranzacția, textul băncii și propunerea, dacă există.</summary>
/// <param name="CanApplyToSimilar">Are contrapartidă recunoscută (nume sau IBAN): se poate salva regula.</param>
public sealed record RegisterExceptionItemDto(
    Guid LedgerEntryId,
    DateOnly Date,
    decimal Amount,
    string BankDetails,
    ClassificationOptionDto? Proposal,
    IReadOnlyList<ClassificationOptionDto> Options,
    bool CanApplyToSimilar);

public sealed record RegisterExceptionGroupDto(RegisterExceptionKind Kind, string Label, IReadOnlyList<RegisterExceptionItemDto> Items);

/// <summary>Excepțiile RJIP ale anului, grupate pe tip; <see cref="Total"/> = numărul din panoul registrelor.</summary>
public sealed record RegisterExceptionsDto(Guid PfaId, int Year, int Total, IReadOnlyList<RegisterExceptionGroupDto> Groups);

/// <summary><c>GET /accounting/pfas/{pfaId}/registers/exceptions?year=</c></summary>
public sealed record GetRegisterExceptionsQuery(Guid PfaId, int Year) : IQuery<RegisterExceptionsDto>;

internal sealed class GetRegisterExceptionsQueryHandler(IApplicationDbContext db) : IQueryHandler<GetRegisterExceptionsQuery, RegisterExceptionsDto>
{
    private static readonly (RegisterExceptionKind Kind, string Label)[] Order =
    [
        (RegisterExceptionKind.UnidentifiedIncome, "Încasări neidentificate"),
        (RegisterExceptionKind.UnclassifiedPayment, "Plăți neclasificate"),
        (RegisterExceptionKind.TransferToConfirm, "Transferuri de confirmat"),
        (RegisterExceptionKind.MissingDocument, "Document lipsă"),
        (RegisterExceptionKind.UnreconciledPayout, "Payout-uri de reconciliat"),
    ];

    public async Task<Result<RegisterExceptionsDto>> Handle(GetRegisterExceptionsQuery query, CancellationToken cancellationToken)
    {
        if (query.Year is < 2000 or > 2100)
        {
            return Result.Failure<RegisterExceptionsDto>(RegisterErrors.InvalidYear);
        }

        var start = new DateOnly(query.Year, 1, 1);
        var end = new DateOnly(query.Year, 12, 31);
        List<LedgerEntry> entries = await RegisterExceptionData.CandidatesAsync(db, query.PfaId, start, end, cancellationToken);
        Dictionary<Guid, BankTransaction> transactions = await RegisterExceptionData.TransactionsAsync(db, entries, cancellationToken);

        List<(RegisterExceptionKind Kind, RegisterExceptionItemDto Item)> items = [.. entries
            .Select(entry => (Entry: entry, Kind: RjipExplanations.ExceptionOf(entry)))
            .Where(pair => pair.Kind is not null)
            .OrderBy(pair => pair.Entry.Date)
            .Select(pair => (pair.Kind!.Value, Item(pair.Entry, transactions.GetValueOrDefault(pair.Entry.BankTransactionId ?? Guid.Empty))))];

        List<RegisterExceptionGroupDto> groups = [.. Order
            .Select(group => new RegisterExceptionGroupDto(group.Kind, group.Label, [.. items.Where(i => i.Kind == group.Kind).Select(i => i.Item)]))
            .Where(group => group.Items.Count > 0)];
        return new RegisterExceptionsDto(query.PfaId, query.Year, items.Count, groups);
    }

    private static RegisterExceptionItemDto Item(LedgerEntry entry, BankTransaction? transaction)
    {
        bool bank = entry.Source == LedgerSource.Bank && transaction is not null;
        string details = transaction is null
            ? entry.Description
            : string.Join(" · ", new[] { transaction.CounterpartyName, transaction.RemittanceInfo }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return new RegisterExceptionItemDto(
            entry.Id,
            entry.Date,
            entry.Amount,
            details.Length > 0 ? details : entry.Description,
            entry.ProposedClassification is { } proposal ? Option(proposal) : null,
            bank ? [.. BankClassifications.OptionsFor(entry.Amount).Select(Option)] : [],
            bank && (CounterpartyRules.NameKey(transaction!.CounterpartyName, transaction.RemittanceInfo).Length >= 3 || transaction.CounterpartyIban is not null));
    }

    private static ClassificationOptionDto Option(BankClassification classification) => new(classification, BankClassifications.Label(classification));
}

internal static class RegisterExceptionData
{
    /// <summary>Înregistrările care pot fi excepții (aceeași regulă ca numărătoarea din panou).</summary>
    public static Task<List<LedgerEntry>> CandidatesAsync(IApplicationDbContext db, Guid pfaId, DateOnly start, DateOnly end, CancellationToken cancellationToken) =>
        db.LedgerEntries.AsNoTracking()
            .Where(e => e.PfaRegistrationId == pfaId && e.Date >= start && e.Date <= end && e.StornoOfEntryId == null && !e.ClosedPeriodFlag &&
                        (e.ReconciliationStatus == ReconciliationStatus.Unmatched ||
                         e.ReconciliationStatus == ReconciliationStatus.NeedsReview ||
                         e.TransactionType == LedgerTransactionType.PlatformSettlement))
            .ToListAsync(cancellationToken);

    public static async Task<Dictionary<Guid, BankTransaction>> TransactionsAsync(IApplicationDbContext db, IEnumerable<LedgerEntry> entries, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. entries.Select(e => e.BankTransactionId).OfType<Guid>().Distinct()];
        return ids.Count == 0
            ? []
            : await db.BankTransactions.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
    }
}

internal static class ClassificationErrors
{
    public static readonly Error NotFound = Error.NotFound("Accounting.LedgerEntryNotFound", "Înregistrarea nu există.");

    public static readonly Error NotBank = Error.Problem("Accounting.ClassifyNotBank", "Se clasifică doar tranzacțiile importate din bancă.");

    public static readonly Error Locked = Error.Conflict("Accounting.ClassifyLocked", "Înregistrarea e într-o lună închisă; corecția se face prin stornare.");

    public static readonly Error WrongDirection = Error.Problem("Accounting.ClassifyDirection", "Clasificarea nu se potrivește cu sensul tranzacției (încasare sau plată).");

    public static readonly Error NoCounterparty = Error.Problem("Accounting.ClassifyNoCounterparty", "Tranzacția nu are contrapartidă recunoscută: regula nu se poate salva.");
}

/// <param name="Classified">Câte înregistrări s-au clasificat, inclusiv cea aleasă.</param>
public sealed record ClassificationResultDto(int Classified, Guid? RuleId);

/// <summary>
/// <c>POST /accounting/ledger-entries/{id}/classify</c> — confirmă propunerea sau alege altă clasificare.
/// Cu <see cref="ApplyToSimilar"/>, salvează regula pe contrapartidă și reclasifică tranzacțiile similare
/// încă nerezolvate din lunile deschise; importurile viitoare o aplică direct.
/// </summary>
public sealed record ClassifyBankEntryCommand(Guid LedgerEntryId, BankClassification Classification, bool ApplyToSimilar) : ICommand<ClassificationResultDto>;

internal sealed class ClassifyBankEntryCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<ClassifyBankEntryCommand, ClassificationResultDto>
{
    public async Task<Result<ClassificationResultDto>> Handle(ClassifyBankEntryCommand command, CancellationToken cancellationToken)
    {
        LedgerEntry? entry = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == command.LedgerEntryId, cancellationToken);
        if (entry is null)
        {
            return Result.Failure<ClassificationResultDto>(ClassificationErrors.NotFound);
        }

        if (entry.Source != LedgerSource.Bank || entry.BankTransactionId is not { } transactionId || entry.StornoOfEntryId is not null)
        {
            return Result.Failure<ClassificationResultDto>(ClassificationErrors.NotBank);
        }

        if (entry.Status == LedgerEntryStatus.Locked || entry.ClosedPeriodFlag)
        {
            return Result.Failure<ClassificationResultDto>(ClassificationErrors.Locked);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, entry.PfaRegistrationId, entry.AccountingPeriod, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<ClassificationResultDto>(writable.Error.Code == "Accounting.PeriodClosed" ? ClassificationErrors.Locked : writable.Error);
        }

        if (!BankClassifications.Fits(command.Classification, entry.Amount))
        {
            return Result.Failure<ClassificationResultDto>(ClassificationErrors.WrongDirection);
        }

        BankTransaction transaction = await db.BankTransactions.AsNoTracking().SingleAsync(t => t.Id == transactionId, cancellationToken);
        LedgerRules rules = await LedgerSupport.RulesAsync(db, entry.PfaRegistrationId, cancellationToken);
        Classify(entry, command.Classification, rules, null);

        int classified = 1;
        CounterpartyClassificationRule? rule = null;
        if (command.ApplyToSimilar)
        {
            string key = CounterpartyRules.NameKey(transaction.CounterpartyName, transaction.RemittanceInfo);
            string? iban = CounterpartyRules.NormalizeIban(transaction.CounterpartyIban);
            if (key.Length < 3 && iban is null)
            {
                return Result.Failure<ClassificationResultDto>(ClassificationErrors.NoCounterparty);
            }

            rule = new CounterpartyClassificationRule
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = entry.PfaRegistrationId,
                Incoming = entry.Amount > 0,
                NameKey = key,
                Iban = iban,
                Classification = command.Classification,
                CreatedByUserId = userContext.UserId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            db.CounterpartyClassificationRules.Add(rule);
            AccountingAudit.Record(db, entry.PfaRegistrationId, nameof(CounterpartyClassificationRule), rule.Id, "CREATE", null,
                new { rule.Incoming, rule.NameKey, rule.Iban, rule.Classification }, null, userContext.UserId);
            classified += await ApplyToOpenAsync(entry, rule, rules, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ClassificationResultDto(classified, rule?.Id);
    }

    /// <summary>Tranzacțiile similare încă nerezolvate din lunile deschise primesc aceeași clasificare.</summary>
    private async Task<int> ApplyToOpenAsync(LedgerEntry chosen, CounterpartyClassificationRule rule, LedgerRules rules, CancellationToken cancellationToken)
    {
        List<string> closed = await db.PfaAccountingPeriods
            .Where(p => p.PfaRegistrationId == chosen.PfaRegistrationId && p.Status == AccountingPeriodStatus.Closed)
            .Select(p => p.Period)
            .ToListAsync(cancellationToken);
        List<LedgerEntry> open = await db.LedgerEntries
            .Where(e => e.PfaRegistrationId == chosen.PfaRegistrationId && e.Id != chosen.Id &&
                        e.Source == LedgerSource.Bank && e.BankTransactionId != null && e.StornoOfEntryId == null &&
                        !e.ClosedPeriodFlag && e.Status != LedgerEntryStatus.Locked && !closed.Contains(e.AccountingPeriod) &&
                        e.SourceDocumentId == null && e.EFacturaMessageId == null &&
                        (e.ReconciliationStatus == ReconciliationStatus.NeedsReview || e.ReconciliationStatus == ReconciliationStatus.Unmatched))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, BankTransaction> transactions = await RegisterExceptionData.TransactionsAsync(db, open, cancellationToken);

        int count = 0;
        foreach (LedgerEntry entry in open)
        {
            if (transactions.TryGetValue(entry.BankTransactionId!.Value, out BankTransaction? transaction) &&
                BankClassifications.Match([rule], entry.Amount, transaction.CounterpartyName, transaction.CounterpartyIban, transaction.RemittanceInfo) is not null)
            {
                Classify(entry, rule.Classification, rules, rule.Id);
                count++;
            }
        }

        return count;
    }

    private void Classify(LedgerEntry entry, BankClassification classification, LedgerRules rules, Guid? ruleId)
    {
        var before = new { entry.TransactionType, entry.Category, entry.ReconciliationStatus, entry.ProposedClassification };
        BankClassifications.Apply(entry, classification, rules, verified: true);
        AccountingAudit.Record(db, entry.PfaRegistrationId, nameof(LedgerEntry), entry.Id, "CLASSIFY", before,
            new { entry.TransactionType, entry.Category, entry.ReconciliationStatus, classification, ruleId }, null, userContext.UserId);
    }
}

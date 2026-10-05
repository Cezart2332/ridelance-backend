using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting;
using Application.Accounting.Documents;
using Application.Accounting.Ledger;
using Application.Expenses.Ocr;
using Domain.Accounting;
using Domain.Documents;
using Domain.Expenses;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Expenses;

public sealed record ExpenseCategoryChoice(string Category, string Label, decimal? Percent);
public sealed record ExpenseBankChoice(Guid Id, DateOnly Date, decimal Amount, string Description);
public sealed record ExpenseSuggestion(string? SupplierName, DateOnly? Date, decimal? Total, decimal? Vat,
    string? DocumentType, string? Category, string? Number, decimal PersonalAmount, string? Currency,
    bool Ready, IReadOnlyList<ExpenseCategoryChoice> Categories, IReadOnlyList<ExpenseBankChoice> Payments,
    Guid? LedgerEntryId, string? PaymentMethod, DateOnly? PaymentDate);
public sealed record GetExpenseSuggestionQuery(Guid PfaId, Guid ExpenseId, decimal? Total = null) : IQuery<ExpenseSuggestion>;

internal sealed class GetExpenseSuggestionQueryHandler(IApplicationDbContext db, IUserContext user,
    ExpenseAccountingService accounting) : IQueryHandler<GetExpenseSuggestionQuery, ExpenseSuggestion>
{
    public async Task<Result<ExpenseSuggestion>> Handle(GetExpenseSuggestionQuery query, CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await db.PfaRegistrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == query.PfaId, cancellationToken);
        User? caller = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == user.UserId, cancellationToken);
        if (pfa is null)
        {
            return Result.Failure<ExpenseSuggestion>(ExpenseErrors.PfaNotFound);
        }

        if (caller is null || caller.IsDeleted || !(caller.Role == UserRole.Admin ||
            caller.Role == UserRole.Contabil && pfa.AssignedContabilId == caller.Id ||
            caller.Role == UserRole.Client && pfa.UserId == caller.Id))
        {
            return Result.Failure<ExpenseSuggestion>(ExpenseErrors.AccessDenied);
        }

        DeductibleExpense? expense = await db.DeductibleExpenses.AsNoTracking().SingleOrDefaultAsync(e => e.Id == query.ExpenseId && e.PfaRegistrationId == query.PfaId, cancellationToken);
        if (expense is null)
        {
            return Result.Failure<ExpenseSuggestion>(Error.NotFound("Expense.NotFound", "Cheltuiala nu există."));
        }
        if (query.Total is > 0)
        {
            expense.AmountRon = query.Total;
        }
        return await accounting.SuggestAsync(expense, cancellationToken);
    }
}

/// <summary>Upload and accountant review share the same payment and tax rules as the registers.</summary>
internal sealed class ExpenseAccountingService(IApplicationDbContext db, IUserContext user)
{
    private static string Digits(string? value) => new((value ?? "").Where(char.IsDigit).ToArray());
    private async Task<Dictionary<string, string?>> FieldsAsync(Guid documentId, CancellationToken cancellationToken) =>
        (await db.ExtractedFields.AsNoTracking().Where(f => f.DocumentId == documentId).ToListAsync(cancellationToken))
        .ToDictionary(f => f.FieldKey, f => f.ConfirmedValue ?? f.AiNormalizedValue ?? f.AiValue, StringComparer.OrdinalIgnoreCase);

    public async Task<ExpenseSuggestion> SuggestAsync(DeductibleExpense expense, CancellationToken cancellationToken)
    {
        Dictionary<string, string?> fields = await FieldsAsync(expense.DocumentId, cancellationToken);
        string? Read(string key) => fields.GetValueOrDefault(key);
        DateOnly? date = expense.ExpenseDate ?? (DateOnly.TryParse(Read("document_date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d) ? d : null);
        decimal? total = expense.AmountRon ?? MoneyParser.Parse(Read("total_amount"));
        string? supplier = expense.SupplierName ?? Read("supplier_name");
        LedgerRules rules = await LedgerSupport.RulesAsync(db, expense.PfaRegistrationId, cancellationToken);
        DateOnly at = date ?? new DateOnly(expense.Year, expense.Month, 1);
        var valid = rules.Categories.Where(r => r.ValidFrom <= at && (r.ValidTo == null || at <= r.ValidTo))
            .GroupBy(r => r.Category).Select(g => g.OrderByDescending(r => r.ValidFrom).First())
            .Where(r => r.Category != DeductibilityService.NonRecoverableVatCategory && r.Category != "DEPRECIATION").ToList();
        string? category = LegacyCategory(expense.ItemName) ?? DeductibilityService.Classify(valid, at, supplier, Read("items_description"))?.Category;
        if (valid.Any(r => r.Category == Read("expense_category")))
        {
            category = Read("expense_category");
        }
        if (valid.Any(r => r.Category == expense.CatalogCategory))
        {
            category = expense.CatalogCategory;
        }
        if (!valid.Any(r => r.Category == category))
        {
            category = null;
        }

        var choices = valid.OrderBy(r => r.Label).Select(r =>
        {
            var preview = new LedgerEntry { Date = at, TransactionType = LedgerTransactionType.Expense, Category = r.Category, Amount = -1 };
            DeductibilityService.Resolve(preview, rules);
            return new ExpenseCategoryChoice(r.Category, r.Label, preview.DeductiblePercent);
        }).ToList();
        ExpenseDocument? document = await db.ExpenseDocuments.AsNoTracking().SingleOrDefaultAsync(e => e.DocumentId == expense.DocumentId && e.PfaRegistrationId == expense.PfaRegistrationId, cancellationToken);
        LedgerEntry? entry = document?.LedgerEntryId is {} id ? await db.LedgerEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && e.PfaRegistrationId == expense.PfaRegistrationId, cancellationToken) : null;
        List<ExpenseBankChoice> payments = total is > 0 ? await db.LedgerEntries.AsNoTracking().Where(e => e.PfaRegistrationId == expense.PfaRegistrationId &&
            e.Source == LedgerSource.Bank && e.BankTransactionId != null && e.Amount == -total &&
            e.TransactionType == LedgerTransactionType.Expense && e.Status != LedgerEntryStatus.Locked && !e.ClosedPeriodFlag &&
            (e.SourceDocumentId == null && e.SettlementGroupId == null || e.Id == (entry == null ? Guid.Empty : entry.Id)))
            .OrderByDescending(e => e.Date).Select(e => new ExpenseBankChoice(e.Id, e.Date, -e.Amount, e.Description)).ToListAsync(cancellationToken) : [];
        HashSet<string> closed = await LedgerSupport.ClosedPeriodsAsync(db, expense.PfaRegistrationId, cancellationToken);
        payments = payments.Where(p => !closed.Contains(LedgerSupport.PeriodOf(p.Date))).ToList();
        return new(supplier, date, total, expense.VatAmount ?? MoneyParser.Parse(Read("vat_amount")), expense.DocumentTypeLabel ?? Read("document_type"),
            entry?.Category ?? category, document?.Number ?? Read("document_number"), entry?.PersonalAmount ?? MoneyParser.Parse(Read("personal_amount")) ?? 0,
            Read("currency") ?? expense.Currency, fields.Count > 0, choices, payments, entry?.Id, entry?.PaymentMethod.ToString(), entry?.Date);
    }

    private static string? LegacyCategory(string text)
    {
        string value = text.ToUpperInvariant();
        if (value.Contains("COMBUSTIBIL") || value.Contains("MOTORIN") || value.Contains("BENZIN") || value.Contains("GPL"))
        {
            return "FUEL";
        }

        if (value.Contains("SPĂL") || value.Contains("SPAL"))
        {
            return "CAR_WASH";
        }

        if (value.Contains("SERVICE") || value.Contains("PIESE") || value.Contains("ANVELOPE"))
        {
            return "CAR_SERVICE";
        }

        if (value.Contains("RCA") || value.Contains("CASCO"))
        {
            return "CAR_INSURANCE";
        }

        if (value.Contains("TELEFON"))
        {
            return "PHONE";
        }
        if (value.Contains("CONTABIL"))
        {
            return "ACCOUNTING";
        }
        if (value.Contains("CHIRIE AUTO"))
        {
            return "CAR_RENTAL";
        }
        if (value.Contains("SOFTWARE"))
        {
            return "SOFTWARE";
        }
        if (value.Contains("ÎNCĂRCARE") || value.Contains("INCARCARE"))
        {
            return "EV_CHARGING";
        }

        return null;
    }

    public async Task<Result> SavePaymentAsync(DeductibleExpense expense, string? method, DateOnly? paidOn,
        Guid? bankId, string? category, decimal personal, string? number, string? reason, CancellationToken cancellationToken)
    {
        ExpenseDocument? existing = await db.ExpenseDocuments.SingleOrDefaultAsync(d => d.PfaRegistrationId == expense.PfaRegistrationId && d.DocumentId == expense.DocumentId, cancellationToken);
        LedgerEntry? entry = existing?.LedgerEntryId is {} entryId ? await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == entryId && e.PfaRegistrationId == expense.PfaRegistrationId, cancellationToken) : null;
        if (entry != null)
        {
            Result editable = await UpdateLedgerEntryCommandHandler.EnsureEditableAsync(db, entry, cancellationToken);
            if (editable.IsFailure)
            {
                return editable;
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result.Failure(AccountingErrors.ReasonRequired);
            }
        }
        // Old callers can save a document without claiming a payment. Never invent a payment from an invoice.
        if (method is null)
        {
            return Result.Success();
        }

        if (method == "Unpaid")
        {
            if (entry != null)
            {
                return Result.Failure(Error.Problem("Expense.PaymentExists", "Există deja o plată. Corectează plata din Încasări și plăți, cu motiv."));
            }

            return Result.Success();
        }
        if (method is not ("Cash" or "Bank") || paidOn == null || expense.ExpenseDate == null || expense.AmountRon is not > 0 ||
            personal < 0 || personal > expense.AmountRon || paidOn < expense.ExpenseDate)
        {
            return Result.Failure(Error.Problem("Expense.InvalidPayment", "Completează data documentului, suma și data/metoda plății. Plata nu poate preceda documentul."));
        }

        if (paidOn > DateOnly.FromDateTime(Application.FiscalProfiles.FiscalProfileService.ToRomania(DateTime.UtcNow)))
        {
            return Result.Failure(Error.Problem("Expense.FuturePayment", "O plată viitoare nu poate fi înregistrată ca efectuată."));
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, expense.PfaRegistrationId, LedgerSupport.PeriodOf(paidOn.Value), cancellationToken);
        if (writable.IsFailure)
        {
            return writable;
        }

        Dictionary<string, string?> fields = await FieldsAsync(expense.DocumentId, cancellationToken);
        string? supplierCui = fields.GetValueOrDefault("supplier_cui");
        string? buyerCui = fields.GetValueOrDefault("beneficiary_cui");
        number ??= fields.GetValueOrDefault("document_number");
        LedgerRules rules = await LedgerSupport.RulesAsync(db, expense.PfaRegistrationId, cancellationToken);
        if (!rules.Categories.Any(r => r.Category == category && r.ValidFrom <= paidOn && (r.ValidTo == null || paidOn <= r.ValidTo)) ||
            category is "DEPRECIATION" or DeductibilityService.NonRecoverableVatCategory)
        {
            return Result.Failure(Error.Problem("Expense.CategoryRequired", "Alege o categorie contabilă valabilă; procentul se calculează din regulile PFA-ului."));
        }

        if (fields.GetValueOrDefault("currency") is {} currency && !string.Equals(currency, "RON", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Problem("Expense.Currency", "Documentul în valută necesită conversie și verificare contabilă înainte de înregistrarea plății în lei."));
        }

        // An uploaded invoice and the ANAF original can justify the same payment, never two payments.
        EFacturaMessage? invoice = null;
        if (!string.IsNullOrWhiteSpace(number) && Digits(supplierCui).Length > 0)
        {
            List<EFacturaMessage> candidates = await db.EFacturaMessages.Where(i => i.PfaRegistrationId == expense.PfaRegistrationId &&
                i.Kind == EFacturaMessageKind.Received && !i.IsCreditNote && i.InvoiceNumber == number &&
                i.IssueDate == expense.ExpenseDate && i.TotalAmount == expense.AmountRon && (i.Currency == null || i.Currency == "RON")).ToListAsync(cancellationToken);
            invoice = candidates.SingleOrDefault(i => Digits(i.SupplierCif) == Digits(supplierCui));
            if (invoice != null && entry == null)
            {
                List<LedgerEntry> paid = await db.LedgerEntries.Where(e => e.PfaRegistrationId == expense.PfaRegistrationId && e.EFacturaMessageId == invoice.Id && e.Amount == -expense.AmountRon).ToListAsync(cancellationToken);
                if (paid.Count == 1)
                {
                    entry = paid[0];
                }
                else if (paid.Count > 1 || invoice.PaidAmount > 0)
                {
                    return Result.Failure(Error.Problem("Expense.AlreadyPaid", "Factura are deja plăți înregistrate. Asociază documentul plăților existente din Încasări și plăți."));
                }
            }
        }
        if (existing == null && !string.IsNullOrWhiteSpace(number) && Digits(supplierCui).Length > 0)
        {
            List<ExpenseDocument> copies = await db.ExpenseDocuments.Where(d => d.PfaRegistrationId == expense.PfaRegistrationId && d.Number == number && d.Date == expense.ExpenseDate && d.Total == expense.AmountRon && d.LedgerEntryId != null).ToListAsync(cancellationToken);
            if (copies.Any(d => Digits(d.MerchantCui) == Digits(supplierCui)))
            {
                return Result.Failure(Error.Problem("Expense.Duplicate", "Acest document justifică deja o plată. Deschide înregistrarea existentă; nu îl înregistra din nou."));
            }
        }
        if (entry != null)
        {
            Result editable = await UpdateLedgerEntryCommandHandler.EnsureEditableAsync(db, entry, cancellationToken);
            if (editable.IsFailure)
            {
                return editable;
            }

            if (entry.BankTransactionId != null && (method != "Bank" || entry.Id != bankId || entry.Amount != -expense.AmountRon || entry.Date != paidOn) ||
                entry.BankTransactionId == null && method != "Cash")
            {
                return Result.Failure(Error.Problem("Expense.PaymentImmutable", "Plata bancară trebuie să păstreze tranzacția, data și suma din extras. Schimbarea metodei se corectează în Încasări și plăți."));
            }
        }
        else if (method == "Bank")
        {
            entry = await db.LedgerEntries.SingleOrDefaultAsync(e => e.Id == bankId && e.PfaRegistrationId == expense.PfaRegistrationId, cancellationToken);
            if (entry == null || entry.Source != LedgerSource.Bank || entry.BankTransactionId == null || entry.Amount != -expense.AmountRon || entry.Date != paidOn ||
                entry.TransactionType != LedgerTransactionType.Expense || entry.SourceDocumentId != null || entry.SettlementGroupId != null ||
                entry.EFacturaMessageId != null && entry.EFacturaMessageId != invoice?.Id)
            {
                return Result.Failure(Error.Problem("Expense.BankRequired", "Selectează plata exactă din OpenBanking. Sincronizează banca dacă plata lipsește."));
            }

            Result editable = await UpdateLedgerEntryCommandHandler.EnsureEditableAsync(db, entry, cancellationToken);
            if (editable.IsFailure)
            {
                return editable;
            }
        }
        else
        {
            entry = LedgerSupport.New(expense.PfaRegistrationId, paidOn.Value, LedgerSource.Upload, $"expense:{expense.Id:N}",
                number ?? expense.ItemName, expense.SupplierName, expense.ItemName, LedgerTransactionType.Expense, PaymentMethod.Cash,
                -expense.AmountRon.Value, "RON", LedgerEntryStatus.AutoImported, new HashSet<string>());
            entry.CreatedByUserId = user.UserId;
            db.LedgerEntries.Add(entry);
        }
        var before = new { entry.Amount, entry.Date, entry.Category, entry.PersonalAmount, entry.SourceDocumentId };
        if (entry.SourceDocumentId != null && entry.SourceDocumentId != expense.DocumentId)
        {
            return Result.Failure(Error.Problem("Expense.PaymentTaken", "Plata are deja alt document justificativ."));
        }

        if (entry.BankTransactionId == null) { entry.Amount = -expense.AmountRon.Value; entry.Date = paidOn.Value; entry.AccountingPeriod = LedgerSupport.PeriodOf(paidOn.Value); }
        entry.DocumentDate = expense.ExpenseDate;
        entry.SourceDocumentId = expense.DocumentId;
        entry.Category = category;
        entry.PersonalAmount = personal;
        string? cui = await db.PfaRegistrations.Where(p => p.Id == expense.PfaRegistrationId).Select(p => p.Cui).SingleAsync(cancellationToken);
        entry.ReconciliationStatus = ReceiptSplit.Confidence(buyerCui, cui, category);
        entry.Description = LedgerSupport.Cut(expense.ItemName, LedgerSupport.DescriptionLength);
        entry.Counterparty = expense.SupplierName is null ? null : LedgerSupport.Cut(expense.SupplierName, LedgerSupport.CounterpartyLength);
        entry.DocumentLabel = LedgerSupport.Cut(number ?? expense.ItemName, LedgerSupport.DocumentLabelLength);
        if (invoice != null && entry.EFacturaMessageId == null)
        {
            entry.EFacturaMessageId = invoice.Id;
            invoice.PaidAmount += expense.AmountRon.Value;
            invoice.PaymentStatus = invoice.PaidAmount >= invoice.TotalAmount ? InvoicePaymentStatus.Paid : InvoicePaymentStatus.PartiallyPaid;
        }
        DeductibilityService.Resolve(entry, rules);
        Result validEntry = await LedgerSupport.ValidateAsync(db, entry, cancellationToken);
        if (validEntry.IsFailure)
        {
            return validEntry;
        }

        if (existing == null)
        {
            existing = new ExpenseDocument { Id = Guid.NewGuid(), PfaRegistrationId = expense.PfaRegistrationId, DocumentId = expense.DocumentId, UploadedByUserId = user.UserId };
            db.ExpenseDocuments.Add(existing);
        }
        existing.Date = expense.ExpenseDate; existing.Total = expense.AmountRon; existing.Merchant = expense.SupplierName;
        existing.MerchantCui = supplierCui; existing.BeneficiaryCui = buyerCui; existing.Number = number; existing.LedgerEntryId = entry.Id;
        expense.CatalogCategory = category!;
        expense.DeductibleLabel = entry.DeductiblePercent is {} percent ? $"{percent:0.##}%" : "Verificare / amortizare";
        AccountingAudit.Record(db, expense.PfaRegistrationId, nameof(LedgerEntry), entry.Id, "UPDATE", before,
            new { entry.Amount, entry.Date, entry.Category, entry.PersonalAmount, entry.SourceDocumentId }, reason ?? "Cheltuială și plată confirmate", user.UserId);
        return Result.Success();
    }
}

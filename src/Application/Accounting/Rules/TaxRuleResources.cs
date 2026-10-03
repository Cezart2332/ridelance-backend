using System.Globalization;
using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Rules;

/// <summary>Resursele de reguli fiscale din §4.5, după calea lor (<c>rules/{kind}</c>).</summary>
public enum TaxRuleKind
{
    Suppliers = 0,
    VatRates = 1,
    D100 = 2,
    AnafSchemas = 3,
    ExpenseCategories = 4,
}

internal static class TaxRuleErrors
{
    public static readonly Error NotFound = Error.NotFound("Accounting.RuleNotFound", "Regula nu există.");

    public static readonly Error InvalidValidity = Error.Problem("Accounting.InvalidValidity", "„Valabil până la” e înainte de „Valabil de la”.");

    public static readonly Error InvalidInput = Error.Problem("Accounting.InvalidRule", "Regula nu are toate câmpurile obligatorii sau are valori invalide.");

    public static Error Overlap(string key, IValidityPeriod clash) => Error.Conflict(
        "Accounting.OverlappingValidity",
        $"Pentru {key} există deja o regulă valabilă {Validity(clash.ValidFrom, clash.ValidTo)}. Închide-o întâi prin „Valabil până la”.");

    private static string Validity(DateOnly from, DateOnly? to) =>
        to is { } end
            ? $"{from.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} – {end.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}"
            : $"din {from.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}";
}

/// <summary><c>GET /accounting/rules/{kind}</c></summary>
public sealed record ListTaxRulesQuery(TaxRuleKind Kind) : IQuery<IReadOnlyList<object>>;

internal sealed class ListTaxRulesQueryHandler(IApplicationDbContext db) : IQueryHandler<ListTaxRulesQuery, IReadOnlyList<object>>
{
    public async Task<Result<IReadOnlyList<object>>> Handle(ListTaxRulesQuery query, CancellationToken cancellationToken) => query.Kind switch
    {
        TaxRuleKind.Suppliers => (await db.SupplierTaxProfiles.AsNoTracking().OrderBy(r => r.VatId).ThenBy(r => r.ValidFrom).ToListAsync(cancellationToken))
            .Select(TaxRuleDtos.Supplier).ToList<object>(),
        TaxRuleKind.VatRates => (await db.VatRates.AsNoTracking().OrderBy(r => r.ValidFrom).ToListAsync(cancellationToken)).Select(TaxRuleDtos.Vat).ToList<object>(),
        TaxRuleKind.D100 => (await db.D100Rules.AsNoTracking().OrderBy(r => r.Code).ThenBy(r => r.ValidFrom).ToListAsync(cancellationToken)).Select(TaxRuleDtos.D100).ToList<object>(),
        TaxRuleKind.AnafSchemas => (await db.AnafDeclarationSchemas.AsNoTracking().OrderBy(r => r.DeclarationType).ThenBy(r => r.ValidFrom).ToListAsync(cancellationToken))
            .Select(TaxRuleDtos.Schema).ToList<object>(),
        _ => (await db.ExpenseCategoryRules.AsNoTracking().OrderBy(r => r.Category).ThenBy(r => r.ValidFrom).ToListAsync(cancellationToken))
            .Select(TaxRuleDtos.Category).ToList<object>(),
    };
}

/// <summary><c>POST /accounting/rules/{kind}</c> și <c>PUT …/{id}</c> — fără ștergere: o regulă se închide prin <c>validTo</c>.</summary>
public sealed record SaveTaxRuleCommand(TaxRuleKind Kind, Guid? Id, JsonElement Input) : ICommand<object>;

/// <summary>
/// Regulile fiscale (spec contabilitate §4.5, F5): valabilitatea e un interval, iar pentru aceeași
/// cheie (furnizorul după cod TVA, cota TVA, codul D100, tipul declarației, categoria) intervalele
/// nu se suprapun. Auditul îl scrie interceptorul.
/// </summary>
internal sealed class SaveTaxRuleCommandHandler(IApplicationDbContext db) : ICommandHandler<SaveTaxRuleCommand, object>
{
    public async Task<Result<object>> Handle(SaveTaxRuleCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return command.Kind switch
            {
                TaxRuleKind.Suppliers => await SaveAsync(
                    command, db.SupplierTaxProfiles, Read<SupplierTaxProfileDto>(command.Input), r => r.Id, id => new SupplierTaxProfile { Id = id }, TaxRuleDtos.Apply,
                    r => r.VatId, i => i.VatId.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(), TaxRuleDtos.Supplier,
                    // F25: regula pe furnizor se leagă de codul fiscal al entității (nu de brand), cu țara și baza
                    // legală (convenția) când are cotă D100.
                    i => i.SupplierName is { Length: > 0 } && i.Country is { Length: 2 } &&
                         Tax.TaxRuleSet.IsEntityKey(i.VatId.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant()) &&
                         (i.D100Rate is null || !string.IsNullOrWhiteSpace(i.Treaty)),
                    cancellationToken),
                TaxRuleKind.VatRates => await SaveAsync(
                    command, db.VatRates, Read<VatRateDto>(command.Input), r => r.Id, id => new VatRate { Id = id }, TaxRuleDtos.Apply,
                    _ => "cota de TVA", _ => "cota de TVA", TaxRuleDtos.Vat, i => i.Rate is >= 0 and <= 100, cancellationToken),
                TaxRuleKind.D100 => await SaveAsync(
                    command, db.D100Rules, Read<D100RuleDto>(command.Input), r => r.Id, id => new D100Rule { Id = id }, TaxRuleDtos.Apply,
                    r => Code(r.Code), i => Code(i.Code), TaxRuleDtos.D100, i => i.Description is not null, cancellationToken),
                TaxRuleKind.AnafSchemas => await SaveAsync(
                    command, db.AnafDeclarationSchemas, Read<AnafDeclarationSchemaDto>(command.Input), r => r.Id, id => new AnafDeclarationSchema { Id = id }, TaxRuleDtos.Apply,
                    r => $"schema {r.DeclarationType}", i => $"schema {i.DeclarationType}", TaxRuleDtos.Schema, i => i.Version is { Length: > 0 }, cancellationToken),
                _ => await SaveAsync(
                    command, db.ExpenseCategoryRules, Read<ExpenseCategoryRuleDto>(command.Input), r => r.Id, id => new ExpenseCategoryRule { Id = id }, TaxRuleDtos.Apply,
                    r => $"categoria {r.Category}", i => $"categoria {(i.Category ?? string.Empty).Trim().ToUpperInvariant()}", TaxRuleDtos.Category,
                    i => i.Category is { Length: > 0 } && i.Label is { Length: > 0 }, cancellationToken),
            };
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return Result.Failure<object>(TaxRuleErrors.InvalidInput);
        }
    }

    /// <summary>Intrarea de la client: forma DTO-ului, fără <c>id</c> (îl dă backendul).</summary>
    private static TInput Read<TInput>(JsonElement input)
    {
        using var document = JsonDocument.Parse(input.GetRawText());
        var withId = new Dictionary<string, JsonElement> { ["id"] = JsonDocument.Parse($"\"{Guid.Empty}\"").RootElement };
        foreach (JsonProperty property in document.RootElement.EnumerateObject().Where(p => p.Name != "id"))
        {
            withId[property.Name] = property.Value.Clone();
        }

        return JsonSerializer.Deserialize<TInput>(JsonSerializer.Serialize(withId), AccountingJson.Options)
            ?? throw new JsonException("Regula lipsește.");
    }

    private async Task<Result<object>> SaveAsync<TEntity, TInput>(
        SaveTaxRuleCommand command,
        DbSet<TEntity> set,
        TInput input,
        Func<TEntity, Guid> idOf,
        Func<Guid, TEntity> create,
        Action<TEntity, TInput> apply,
        Func<TEntity, string> keyOfEntity,
        Func<TInput, string> keyOfInput,
        Func<TEntity, object> dto,
        Func<TInput, bool> valid,
        CancellationToken cancellationToken)
        where TEntity : class, IValidityPeriod, IAccountingRecord
    {
        (DateOnly from, DateOnly? to) = ValidityOf(input);
        if (to is { } end && end < from)
        {
            return Result.Failure<object>(TaxRuleErrors.InvalidValidity);
        }

        if (!valid(input))
        {
            return Result.Failure<object>(TaxRuleErrors.InvalidInput);
        }

        List<TEntity> all = await set.ToListAsync(cancellationToken);
        TEntity? entity = null;
        if (command.Id is { } id && (entity = all.FirstOrDefault(e => idOf(e) == id)) is null)
        {
            return Result.Failure<object>(TaxRuleErrors.NotFound);
        }

        string key = keyOfInput(input);
        TEntity? clash = all.FirstOrDefault(other =>
            idOf(other) != command.Id &&
            keyOfEntity(other) == key &&
            other.ValidFrom <= (to ?? DateOnly.MaxValue) && from <= (other.ValidTo ?? DateOnly.MaxValue));
        if (clash is not null)
        {
            return Result.Failure<object>(TaxRuleErrors.Overlap(key, clash));
        }

        if (entity is null)
        {
            entity = create(Guid.NewGuid());
            set.Add(entity);
        }

        apply(entity, input);
        await db.SaveChangesAsync(cancellationToken);
        return dto(entity);
    }

    private static string Code(D100RuleCode code) => AccountingJson.Serialize(code).Trim('"');

    private static (DateOnly From, DateOnly? To) ValidityOf<TInput>(TInput input)
    {
        Type type = typeof(TInput);
        var from = (DateOnly)type.GetProperty("ValidFrom")!.GetValue(input)!;
        var to = (DateOnly?)type.GetProperty("ValidTo")!.GetValue(input);
        return (from, to);
    }
}

/// <summary>Maparea regulă ↔ DTO; câmpurile vin normalizate (coduri cu majuscule, text fără spații la capete).</summary>
/// <summary>
/// <c>DELETE /accounting/rules/suppliers/{id}</c> — ștergere logică a unui furnizor adăugat greșit.
/// Un furnizor care apare deja într-o declarație nu se șterge: se închide cu „Valabil până la”.
/// </summary>
public sealed record DeleteSupplierCommand(Guid Id) : ICommand;

internal sealed class DeleteSupplierCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<DeleteSupplierCommand>
{
    public async Task<Result> Handle(DeleteSupplierCommand command, CancellationToken cancellationToken)
    {
        SupplierTaxProfile? supplier = await db.SupplierTaxProfiles.SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure(TaxRuleErrors.NotFound);
        }

        bool declared = await db.DeclarationLines.AnyAsync(line => line.SupplierVatId == supplier.VatId, cancellationToken);
        if (declared)
        {
            return Result.Failure(Error.Conflict(
                "Accounting.SupplierInUse",
                $"{supplier.SupplierName} ({supplier.VatId}) apare în declarații generate și nu se poate șterge. Închide-l cu „Valabil până la”."));
        }

        supplier.DeletedAtUtc = DateTime.UtcNow;
        supplier.DeletedByUserId = userContext.UserId;
        AccountingAudit.Record(
            db, null, nameof(SupplierTaxProfile), supplier.Id, "DELETE",
            new { supplier.SupplierName, supplier.VatId, supplier.ValidFrom, supplier.ValidTo }, null, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class TaxRuleDtos
{
    public static object Supplier(SupplierTaxProfile r) => new SupplierTaxProfileDto(
        r.Id, r.SupplierName, r.Country, r.VatId, r.IncomeType, r.Treaty, r.D100Rate, r.D100RateConfirmed, r.ValidFrom, r.ValidTo,
        r.ResidenceCertValidFrom, r.ResidenceCertValidTo,
        r.ResidenceCertDocumentId is { } document ? new StoredFileRef(document, "certificat-rezidenta", "application/pdf", 0, string.Empty) : null,
        r.Note);

    public static void Apply(SupplierTaxProfile r, SupplierTaxProfileDto i)
    {
        r.SupplierName = i.SupplierName.Trim();
        r.Country = i.Country.Trim().ToUpperInvariant();
        r.VatId = i.VatId.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        r.IncomeType = string.IsNullOrWhiteSpace(i.IncomeType) ? "COMMISSION" : i.IncomeType.Trim().ToUpperInvariant();
        r.Treaty = string.IsNullOrWhiteSpace(i.Treaty) ? null : i.Treaty.Trim();
        r.D100Rate = i.D100Rate;
        r.D100RateConfirmed = i.D100RateConfirmed;
        r.ValidFrom = i.ValidFrom;
        r.ValidTo = i.ValidTo;
        r.ResidenceCertValidFrom = i.ResidenceCertValidFrom;
        r.ResidenceCertValidTo = i.ResidenceCertValidTo;
        r.ResidenceCertDocumentId = i.ResidenceCertFile?.Id is { } id && id != Guid.Empty ? id : r.ResidenceCertDocumentId;
        r.Note = string.IsNullOrWhiteSpace(i.Note) ? null : i.Note.Trim();
    }

    public static object Vat(VatRate r) => new VatRateDto(r.Id, r.Rate, r.ValidFrom, r.ValidTo);

    public static void Apply(VatRate r, VatRateDto i)
    {
        r.Rate = i.Rate;
        r.ValidFrom = i.ValidFrom;
        r.ValidTo = i.ValidTo;
    }

    public static object D100(D100Rule r) => new D100RuleDto(
        r.Id, r.Code, r.Enabled, r.Description, r.PendingConfirmation, JsonDocument.Parse(r.ParametersJson).RootElement.Clone(), r.ValidFrom, r.ValidTo);

    public static void Apply(D100Rule r, D100RuleDto i)
    {
        r.Code = i.Code;
        r.Enabled = i.Enabled;
        r.Description = i.Description.Trim();
        r.PendingConfirmation = i.PendingConfirmation;
        r.ParametersJson = i.Parameters.ValueKind == JsonValueKind.Object ? i.Parameters.GetRawText() : "{}";
        r.ValidFrom = i.ValidFrom;
        r.ValidTo = i.ValidTo;
    }

    /// <summary>XSD-ul e încorporat în aplicație (B4): fișierul se arată după cale, nu ca document încărcat.</summary>
    public static object Schema(AnafDeclarationSchema r) => new AnafDeclarationSchemaDto(
        r.Id,
        r.DeclarationType,
        r.Version,
        r.XsdPath is { } path ? new StoredFileRef(Guid.Empty, Path.GetFileName(path), "application/xml", 0, string.Empty) : null,
        r.ValidatorVersion,
        r.ValidFrom,
        r.ValidTo);

    public static void Apply(AnafDeclarationSchema r, AnafDeclarationSchemaDto i)
    {
        r.DeclarationType = i.DeclarationType;
        r.Version = i.Version.Trim();
        r.ValidatorVersion = string.IsNullOrWhiteSpace(i.ValidatorVersion) ? null : i.ValidatorVersion.Trim();
        r.ValidFrom = i.ValidFrom;
        r.ValidTo = i.ValidTo;
    }

    public static object Category(ExpenseCategoryRule r) => new ExpenseCategoryRuleDto(
        r.Id, r.Category, r.Label, r.VehicleRelated, r.DefaultDeductibility, r.CounterpartyPattern, r.ValidFrom, r.ValidTo);

    public static void Apply(ExpenseCategoryRule r, ExpenseCategoryRuleDto i)
    {
        r.Category = i.Category.Trim().ToUpperInvariant();
        r.Label = i.Label.Trim();
        r.VehicleRelated = i.VehicleRelated;
        r.DefaultDeductibility = i.DefaultDeductibility;
        r.CounterpartyPattern = string.IsNullOrWhiteSpace(i.CounterpartyPattern) ? null : i.CounterpartyPattern.Trim();
        r.ValidFrom = i.ValidFrom;
        r.ValidTo = i.ValidTo;
    }
}

/// <summary><c>GET /accounting/rules/exchange-rates?currency&amp;date</c> — ultimul curs publicat la data cerută sau înainte.</summary>
public sealed record GetExchangeRateQuery(string Currency, DateOnly Date) : IQuery<ExchangeRateDto?>;

internal sealed class GetExchangeRateQueryHandler(IApplicationDbContext db) : IQueryHandler<GetExchangeRateQuery, ExchangeRateDto?>
{
    public async Task<Result<ExchangeRateDto?>> Handle(GetExchangeRateQuery query, CancellationToken cancellationToken)
    {
        string currency = query.Currency.Trim().ToUpperInvariant();
        ExchangeRate? rate = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.Currency == currency && r.Date <= query.Date)
            .OrderByDescending(r => r.Date)
            .FirstOrDefaultAsync(cancellationToken);
        return Result.Success(rate is null ? null : new ExchangeRateDto(rate.Currency, rate.Date, rate.Rate, rate.Source));
    }
}

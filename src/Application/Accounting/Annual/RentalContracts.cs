using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Accounting.Tax;
using Domain.Accounting;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Annual;

internal static class RentalContracts
{
    public static readonly Error NotFound = Error.NotFound("Accounting.RentalContractNotFound", "Contractul de închiriere nu există.");

    public static readonly Error InvalidCnp = Error.Problem("Accounting.RentalContractCnp", "CNP-ul proprietarului nu e valid.");

    public static readonly Error InvalidAmount = Error.Problem("Accounting.RentalContractAmount", "Suma trebuie să fie pozitivă.");

    /// <summary>F43: regula contractului e de chirie, nu de nerezident.</summary>
    public static Error WrongRule(string type) => Error.Problem(
        "Accounting.RentalContractRule", $"Regula aleasă e de tip {type}; contractul de chirie cere o regulă de reținere pentru chirie.");

    public static string MaskCnp(ISecretProtector secrets, string encrypted) =>
        string.IsNullOrEmpty(encrypted) ? string.Empty : CnpValidator.Mask(secrets.Unprotect(encrypted));
}

public sealed record RentalContractDto(
    Guid Id,
    string OwnerName,
    string OwnerCnpMasked,
    string ContractNumber,
    DateOnly ContractDate,
    decimal GrossRent,
    string PaymentFrequency,
    Guid WithholdingRuleId,
    IReadOnlyList<RentPaymentDto> Payments);

public sealed record RentPaymentDto(Guid Id, DateOnly PaymentDate, decimal GrossAmount, decimal Tax, bool WithholdOnPayment, bool RuleConfirmed);

/// <summary><c>GET /accounting/pfas/{pfaId}/rental-contracts</c> — gol pentru PFA-urile fără chirie (F40).</summary>
public sealed record ListRentalContractsQuery(Guid PfaId) : IQuery<IReadOnlyList<RentalContractDto>>;

internal sealed class ListRentalContractsQueryHandler(IApplicationDbContext db, ISecretProtector secrets)
    : IQueryHandler<ListRentalContractsQuery, IReadOnlyList<RentalContractDto>>
{
    public async Task<Result<IReadOnlyList<RentalContractDto>>> Handle(ListRentalContractsQuery query, CancellationToken cancellationToken)
    {
        List<RentalContract> contracts = await db.RentalContracts.AsNoTracking()
            .Where(c => c.PfaRegistrationId == query.PfaId)
            .OrderBy(c => c.ContractDate)
            .ToListAsync(cancellationToken);
        if (contracts.Count == 0)
        {
            return Array.Empty<RentalContractDto>();
        }

        List<Guid> ids = [.. contracts.Select(c => c.Id)];
        List<RentPayment> payments = await db.RentPayments.AsNoTracking().Where(p => ids.Contains(p.RentalContractId)).ToListAsync(cancellationToken);
        Dictionary<Guid, TaxRule> rules = await db.TaxRules.AsNoTracking().Where(r => r.RuleType == TaxRuleTypes.RentWithholding).ToDictionaryAsync(r => r.Id, cancellationToken);
        return contracts.Select(c =>
        {
            string masked = RentalContracts.MaskCnp(secrets, c.OwnerCnpEncrypted);
            return new RentalContractDto(
                c.Id, c.OwnerName, masked, c.ContractNumber, c.ContractDate, c.GrossRent, c.PaymentFrequency, c.WithholdingRuleId,
                [.. payments.Where(p => p.RentalContractId == c.Id).OrderBy(p => p.PaymentDate).Select(p =>
                {
                    RentWithholding withholding = RentEngine.Withhold(c, p, rules[c.WithholdingRuleId], masked);
                    return new RentPaymentDto(p.Id, p.PaymentDate, p.GrossAmount, withholding.Tax, withholding.WithholdOnPayment, withholding.RuleConfirmed);
                })]);
        }).ToList();
    }
}

/// <summary><c>POST /accounting/pfas/{pfaId}/rental-contracts</c> — CNP-ul se salvează criptat.</summary>
public sealed record CreateRentalContractCommand(
    Guid PfaId,
    string OwnerName,
    string OwnerCnp,
    string ContractNumber,
    DateOnly ContractDate,
    decimal GrossRent,
    string PaymentFrequency,
    Guid WithholdingRuleId) : ICommand<Guid>;

internal sealed class CreateRentalContractCommandHandler(IApplicationDbContext db, ISecretProtector secrets, IUserContext userContext)
    : ICommandHandler<CreateRentalContractCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateRentalContractCommand command, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == command.PfaId, cancellationToken))
        {
            return Result.Failure<Guid>(AccountingErrors.PfaNotFound);
        }

        string cnp = command.OwnerCnp?.Trim() ?? string.Empty;
        if (!CnpValidator.IsValid(cnp))
        {
            return Result.Failure<Guid>(RentalContracts.InvalidCnp);
        }

        if (command.GrossRent <= 0)
        {
            return Result.Failure<Guid>(RentalContracts.InvalidAmount);
        }

        TaxRule? rule = await db.TaxRules.AsNoTracking().SingleOrDefaultAsync(r => r.Id == command.WithholdingRuleId, cancellationToken);
        if (rule is null || rule.RuleType != TaxRuleTypes.RentWithholding)
        {
            return Result.Failure<Guid>(RentalContracts.WrongRule(rule?.RuleType ?? "necunoscut"));
        }

        var contract = new RentalContract
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaId,
            OwnerName = command.OwnerName.Trim(),
            OwnerCnpEncrypted = secrets.Protect(cnp),
            ContractNumber = command.ContractNumber.Trim(),
            ContractDate = command.ContractDate,
            GrossRent = command.GrossRent,
            PaymentFrequency = command.PaymentFrequency.Trim().ToUpperInvariant(),
            WithholdingRuleId = rule.Id,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.RentalContracts.Add(contract);
        AccountingAudit.Record(db, command.PfaId, nameof(RentalContract), contract.Id, "CREATE", null,
            new { contract.OwnerName, contract.ContractNumber, contract.ContractDate, contract.GrossRent, contract.PaymentFrequency, contract.WithholdingRuleId }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return contract.Id;
    }
}

/// <summary><c>POST /accounting/rental-contracts/{id}/payments</c> — plata determină luna rândului D100 (F41).</summary>
public sealed record AddRentPaymentCommand(Guid ContractId, DateOnly PaymentDate, decimal GrossAmount) : ICommand<Guid>;

internal sealed class AddRentPaymentCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<AddRentPaymentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddRentPaymentCommand command, CancellationToken cancellationToken)
    {
        RentalContract? contract = await db.RentalContracts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == command.ContractId, cancellationToken);
        if (contract is null)
        {
            return Result.Failure<Guid>(RentalContracts.NotFound);
        }

        if (command.GrossAmount <= 0)
        {
            return Result.Failure<Guid>(RentalContracts.InvalidAmount);
        }

        Result writable = await Documents.PlatformDocumentSupport.EnsureWritableAsync(
            db, contract.PfaRegistrationId, Ledger.LedgerSupport.PeriodOf(command.PaymentDate), cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<Guid>(writable.Error);
        }

        var payment = new RentPayment
        {
            Id = Guid.NewGuid(),
            RentalContractId = contract.Id,
            PaymentDate = command.PaymentDate,
            GrossAmount = command.GrossAmount,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.RentPayments.Add(payment);
        AccountingAudit.Record(db, contract.PfaRegistrationId, nameof(RentPayment), payment.Id, "CREATE", null,
            new { payment.PaymentDate, payment.GrossAmount, contract.ContractNumber }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return payment.Id;
    }
}

/// <summary>
/// Rândurile de chirie ale lunii pentru D100 (F41): fiecare plată din lună, cu reținerea după regula
/// contractului, dacă regula o cere la plată.
/// </summary>
internal static class RentLines
{
    public static async Task<ILookup<Guid, RentWithholding>> LoadAsync(
        IApplicationDbContext db, ISecretProtector? secrets, IReadOnlyCollection<Guid> pfaIds, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        List<Guid> ids = [.. pfaIds];
        var rows = await db.RentPayments.AsNoTracking()
            .Where(p => p.PaymentDate >= start && p.PaymentDate <= end)
            .Join(db.RentalContracts.Where(c => ids.Contains(c.PfaRegistrationId)), p => p.RentalContractId, c => c.Id, (p, c) => new { Payment = p, Contract = c })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return Array.Empty<RentWithholding>().ToLookup(_ => Guid.Empty);
        }

        Dictionary<Guid, TaxRule> rules = await db.TaxRules.AsNoTracking().Where(r => r.RuleType == TaxRuleTypes.RentWithholding).ToDictionaryAsync(r => r.Id, cancellationToken);
        return rows
            .OrderBy(r => r.Payment.PaymentDate)
            .Select(r => (r.Contract.PfaRegistrationId, Line: RentEngine.Withhold(
                r.Contract,
                r.Payment,
                rules.GetValueOrDefault(r.Contract.WithholdingRuleId)
                    ?? throw new TaxRuleConfigurationException($"Regula de reținere a contractului {r.Contract.ContractNumber} nu e o regulă de chirie."),
                secrets is null ? string.Empty : RentalContracts.MaskCnp(secrets, r.Contract.OwnerCnpEncrypted))))
            .Where(r => r.Line.WithholdOnPayment)
            .ToLookup(r => r.PfaRegistrationId, r => r.Line);
    }
}

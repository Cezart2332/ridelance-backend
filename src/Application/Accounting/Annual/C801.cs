using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Tax;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Annual;

/// <summary>
/// Datele C801 (spec declarații F60): CUI-ul PFA, casa de marcat (AMEF), tipul de activitate din
/// regulile fiscale și numărul de înmatriculare; plus starea, documentul și NUI-ul (F61).
/// </summary>
public sealed record C801Dto(
    Guid PfaId,
    string? Cui,
    CashRegisterStatus CashRegisterStatus,
    DateOnly? ActivationDate,
    string? ActivityType,
    string? VehiclePlate,
    C801Status Status,
    Guid? DocumentId,
    string? NuiNumber,
    IReadOnlyList<string> Missing);

internal static class C801Errors
{
    public static readonly Error NoCashRegister = Error.NotFound("Accounting.C801NoCashRegister", "PFA-ul nu are casă de marcat.");

    public static readonly Error DocumentRequired = Error.Problem("Accounting.C801DocumentRequired", "Pentru C801 depusă e nevoie de document (cererea sau confirmarea furnizorului).");

    public static readonly Error NuiRequired = Error.Problem("Accounting.C801NuiRequired", "NUI-ul e obligatoriu.");
}

internal static class C801Data
{
    /// <summary>Tipul de activitate din C801 pentru transportul alternativ: regula fiscală, nu o constantă.</summary>
    public static string? ActivityType(TaxRuleSet rules, DateOnly date) =>
        rules.Find(TaxRuleTypes.FormCode, "RO", date, new TaxRuleContext("C801", IncomeType: "RIDESHARING"))?.Formula;

    public static async Task<Result<C801Dto>> DtoAsync(IApplicationDbContext db, Guid pfaId, CancellationToken cancellationToken)
    {
        CashRegisterState? state = await db.CashRegisterStates.AsNoTracking().SingleOrDefaultAsync(c => c.PfaRegistrationId == pfaId, cancellationToken);
        if (state is null || !state.CashEnabled && !state.CashRequested)
        {
            return Result.Failure<C801Dto>(C801Errors.NoCashRegister);
        }

        string? cui = await db.PfaRegistrations.AsNoTracking().Where(p => p.Id == pfaId).Select(p => p.Cui).SingleOrDefaultAsync(cancellationToken);
        string? activity = ActivityType(await TaxRuleSet.LoadAsync(db, cancellationToken), DateOnly.FromDateTime(DateTime.UtcNow));
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(cui))
        {
            missing.Add("CUI-ul PFA");
        }

        if (activity is null)
        {
            missing.Add("Tipul de activitate C801 în regulile fiscale");
        }

        if (string.IsNullOrWhiteSpace(state.VehiclePlate))
        {
            missing.Add("Numărul de înmatriculare");
        }

        return new C801Dto(pfaId, cui, state.Status, state.ActivationDate, activity, state.VehiclePlate, state.C801Status, state.C801DocumentId, state.NuiNumber, missing);
    }
}

/// <summary><c>GET /accounting/pfas/{pfaId}/c801</c></summary>
public sealed record GetC801Query(Guid PfaId) : IQuery<C801Dto>;

internal sealed class GetC801QueryHandler(IApplicationDbContext db) : IQueryHandler<GetC801Query, C801Dto>
{
    public Task<Result<C801Dto>> Handle(GetC801Query query, CancellationToken cancellationToken) => C801Data.DtoAsync(db, query.PfaId, cancellationToken);
}

/// <summary>
/// <c>PUT /accounting/pfas/{pfaId}/c801</c> — F61: RIDElance păstrează starea, documentul și NUI-ul;
/// când furnizorul casei a depus C801, nu se generează nimic.
/// </summary>
public sealed record UpdateC801Command(Guid PfaId, C801Status Status, Guid? DocumentId, string? NuiNumber, string? VehiclePlate) : ICommand<C801Dto>;

internal sealed class UpdateC801CommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<UpdateC801Command, C801Dto>
{
    public async Task<Result<C801Dto>> Handle(UpdateC801Command command, CancellationToken cancellationToken)
    {
        CashRegisterState? state = await db.CashRegisterStates.SingleOrDefaultAsync(c => c.PfaRegistrationId == command.PfaId, cancellationToken);
        if (state is null)
        {
            return Result.Failure<C801Dto>(C801Errors.NoCashRegister);
        }

        string? nui = string.IsNullOrWhiteSpace(command.NuiNumber) ? null : command.NuiNumber.Trim();
        if (command.Status is C801Status.FiledByProvider or C801Status.Filed or C801Status.NuiReceived && command.DocumentId is null)
        {
            return Result.Failure<C801Dto>(C801Errors.DocumentRequired);
        }

        if (command.Status == C801Status.NuiReceived && nui is null)
        {
            return Result.Failure<C801Dto>(C801Errors.NuiRequired);
        }

        var before = new { state.C801Status, state.C801DocumentId, state.NuiNumber, state.VehiclePlate };
        state.C801Status = command.Status;
        state.C801DocumentId = command.DocumentId;
        state.NuiNumber = nui;
        state.VehiclePlate = string.IsNullOrWhiteSpace(command.VehiclePlate) ? state.VehiclePlate : command.VehiclePlate.Trim().ToUpperInvariant();
        state.UpdatedAtUtc = DateTime.UtcNow;
        AccountingAudit.Record(db, command.PfaId, nameof(CashRegisterState), command.PfaId, "C801", before,
            new { state.C801Status, state.C801DocumentId, state.NuiNumber, state.VehiclePlate }, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return await C801Data.DtoAsync(db, command.PfaId, cancellationToken);
    }
}

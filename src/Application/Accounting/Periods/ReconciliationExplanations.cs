using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Accounting;
using SharedKernel;

namespace Application.Accounting.Periods;

/// <summary>
/// <c>POST /accounting/pfas/{pfaId}/periods/{period}/explanations</c> — <c>{ control, note }</c>
/// (spec registre §7): diferența Z vs cash platformă sau payout-urile nereconciliate care rămân, cu
/// explicația Adminului; controlul lunii trece. Explicațiile nu se șterg; ultima contează.
/// </summary>
public sealed record ExplainReconciliationControlCommand(Guid PfaId, string Period, ReconciliationControl Control, string? Note) : ICommand;

internal sealed class ExplainReconciliationControlCommandHandler(IApplicationDbContext db, IUserContext userContext)
    : ICommandHandler<ExplainReconciliationControlCommand>
{
    public static readonly Error NotExplainable = Error.Problem(
        "Accounting.ControlNotExplainable", "Doar diferența Z vs cash platformă și payout-urile nereconciliate se pot explica.");

    public async Task<Result> Handle(ExplainReconciliationControlCommand command, CancellationToken cancellationToken)
    {
        if (!Documents.PlatformDocumentSupport.IsValidPeriod(command.Period))
        {
            return Result.Failure(AccountingErrors.InvalidPeriod);
        }

        if (!MonthReconciliation.Explainable.Contains(command.Control))
        {
            return Result.Failure(NotExplainable);
        }

        if (string.IsNullOrWhiteSpace(command.Note))
        {
            return Result.Failure(AccountingErrors.ReasonRequired);
        }

        Result writable = await Documents.PlatformDocumentSupport.EnsureWritableAsync(db, command.PfaId, command.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return writable;
        }

        var explanation = new ReconciliationExplanation
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaId,
            Period = command.Period,
            Control = command.Control.ToString(),
            Note = command.Note.Trim(),
            CreatedByUserId = userContext.UserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.ReconciliationExplanations.Add(explanation);
        AccountingAudit.Record(db, command.PfaId, nameof(ReconciliationExplanation), explanation.Id, "EXPLAIN", null,
            new { command.Period, explanation.Control }, explanation.Note, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

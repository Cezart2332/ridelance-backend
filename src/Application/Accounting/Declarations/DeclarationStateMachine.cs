using Application.Accounting.Months;
using Domain.Accounting;

namespace Application.Accounting.Declarations;

/// <summary>
/// Mașina de stări a unei versiuni de declarație (spec contabilitate §3.2, B5), oglinda lui
/// <c>declarationWorkflow.ts</c> din frontend. Orice altă tranziție e refuzată cu 409.
/// </summary>
internal static class DeclarationStateMachine
{
    /// <summary>Tranzițiile de status permise, exact lista din §3.2.</summary>
    public static readonly IReadOnlyDictionary<DeclarationStatus, DeclarationStatus[]> Transitions = new Dictionary<DeclarationStatus, DeclarationStatus[]>
    {
        [DeclarationStatus.Draft] = [DeclarationStatus.Generated],
        [DeclarationStatus.Generated] = [DeclarationStatus.Validated, DeclarationStatus.ValidationFailed],
        [DeclarationStatus.ValidationFailed] = [DeclarationStatus.Generated],
        [DeclarationStatus.Validated] = [DeclarationStatus.ReadyToSign],
        [DeclarationStatus.ReadyToSign] = [DeclarationStatus.Signed],
        [DeclarationStatus.Signed] = [DeclarationStatus.Submitted],
        [DeclarationStatus.Submitted] = [DeclarationStatus.Accepted, DeclarationStatus.Rejected],
        // Din ACCEPTED versiunea nu se mai mișcă: corecția e o versiune nouă, RECTIFICATIVE.
        [DeclarationStatus.Accepted] = [],
        [DeclarationStatus.Rejected] = [DeclarationStatus.Generated],
    };

    /// <summary>Din ce status pornește fiecare acțiune a endpoint-ului <c>transitions</c>.</summary>
    public static readonly IReadOnlyDictionary<DeclarationAction, DeclarationStatus[]> ActionSources = new Dictionary<DeclarationAction, DeclarationStatus[]>
    {
        [DeclarationAction.Validate] = [DeclarationStatus.Generated],
        [DeclarationAction.MarkSigned] = [DeclarationStatus.ReadyToSign],
        [DeclarationAction.MarkSubmitted] = [DeclarationStatus.Signed],
        [DeclarationAction.MarkRejected] = [DeclarationStatus.Submitted],
        [DeclarationAction.Regenerate] = [DeclarationStatus.ValidationFailed, DeclarationStatus.Rejected],
    };

    public static bool CanTransition(DeclarationStatus from, DeclarationStatus to) =>
        Transitions.TryGetValue(from, out DeclarationStatus[]? targets) && targets.Contains(to);

    public static bool IsAllowed(DeclarationStatus status, DeclarationAction action) => ActionSources[action].Contains(status);

    /// <summary>Trece versiunea în <paramref name="to"/> și notează tranziția în istoric.</summary>
    /// <exception cref="InvalidOperationException">Tranziția nu e în §3.2 (greșeală de program: apelantul verifică înainte).</exception>
    public static void Move(DeclarationVersion version, DeclarationStatus to, Guid? userId, string? note)
    {
        if (!CanTransition(version.Status, to))
        {
            throw new InvalidOperationException($"Tranziția {version.Status} → {to} nu e permisă.");
        }

        List<StatusHistoryRecord> history = AccountingJson.Deserialize<List<StatusHistoryRecord>>(version.StatusHistoryJson, []);
        history.Add(new StatusHistoryRecord(version.Status, to, DateTime.UtcNow, userId, note));
        version.StatusHistoryJson = AccountingJson.Serialize(history);
        version.Status = to;
    }
}

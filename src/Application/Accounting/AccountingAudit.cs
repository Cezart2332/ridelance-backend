using Application.Abstractions.Data;
using Domain.Accounting;

namespace Application.Accounting;

/// <summary>
/// Scrie în <see cref="AuditLog"/> (spec §0 pct. 7): cine, când, valoarea veche, cea nouă, motivul.
/// Intrarea se salvează în aceeași tranzacție cu modificarea, la <c>SaveChangesAsync</c>.
/// </summary>
internal static class AccountingAudit
{
    public static void Record(
        IApplicationDbContext db,
        Guid? pfaId,
        string entity,
        Guid entityId,
        string action,
        object? before,
        object? after,
        string? reason,
        Guid? userId)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaId,
            Entity = entity,
            EntityId = entityId.ToString(),
            Action = action,
            BeforeJson = before is null ? null : AccountingJson.Serialize(before),
            AfterJson = after is null ? null : AccountingJson.Serialize(after),
            Reason = reason,
            UserId = userId,
            AtUtc = DateTime.UtcNow,
        });
    }
}

using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Audit;

/// <summary><c>GET /accounting/pfas/{pfaId}/audit?from&amp;to&amp;entity</c> — cele mai noi întâi.</summary>
public sealed record ListPfaAuditQuery(Guid PfaId, DateOnly? From, DateOnly? To, string? Entity) : IQuery<IReadOnlyList<AuditEntryDto>>;

/// <summary>Jurnalul de audit al unui PFA (spec §0 pct. 7): cine, când, ce valoare veche și nouă, motivul.</summary>
internal sealed class ListPfaAuditQueryHandler(IApplicationDbContext db) : IQueryHandler<ListPfaAuditQuery, IReadOnlyList<AuditEntryDto>>
{
    /// <summary>Plafonul unui răspuns; filtrele pe interval îngustează restul.</summary>
    private const int Limit = 500;

    public async Task<Result<IReadOnlyList<AuditEntryDto>>> Handle(ListPfaAuditQuery query, CancellationToken cancellationToken)
    {
        if (!await db.PfaRegistrations.AnyAsync(p => p.Id == query.PfaId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<AuditEntryDto>>(AccountingErrors.PfaNotFound);
        }

        IQueryable<AuditLog> logs = db.AuditLogs.AsNoTracking().Where(a => a.PfaRegistrationId == query.PfaId);
        if (query.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            logs = logs.Where(a => a.AtUtc >= start);
        }

        if (query.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            logs = logs.Where(a => a.AtUtc < end);
        }

        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            logs = logs.Where(a => a.Entity == query.Entity);
        }

        List<AuditLog> rows = await logs.OrderByDescending(a => a.AtUtc).Take(Limit).ToListAsync(cancellationToken);
        Dictionary<Guid, UserRef> users = await PlatformDocumentSupport.UsersAsync(db, rows.Select(a => a.UserId), cancellationToken);

        return rows.Select(a => new AuditEntryDto(
            a.Id,
            a.Entity,
            a.EntityId,
            a.Action,
            Json(a.BeforeJson),
            Json(a.AfterJson),
            a.Reason,
            a.UserId is { } id && users.TryGetValue(id, out UserRef? user) ? user : new UserRef(Guid.Empty, "Sistem"),
            a.AtUtc)).ToList();
    }

    private static JsonElement? Json(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : JsonSerializer.Deserialize<JsonElement>(value);
}

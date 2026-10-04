using Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Pfas;

/// <summary>Read access for the authenticated owner, independent of staff permissions.</summary>
public static class ClientAccountingScope
{
    public static Task<Guid?> PfaIdAsync(IApplicationDbContext db, Guid userId, CancellationToken ct) =>
        db.PfaRegistrations.AsNoTracking().Where(p => p.UserId == userId && p.User.DeletedAtUtc == null)
            .OrderByDescending(p => p.CreatedAtUtc).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);

    public static Task<bool> OwnsInvoiceAsync(IApplicationDbContext db, Guid pfaId, Guid messageId, CancellationToken ct) =>
        db.EFacturaMessages.AnyAsync(m => m.Id == messageId && m.PfaRegistrationId == pfaId, ct);

    public static Task<bool> OwnsSpvMessageAsync(IApplicationDbContext db, Guid pfaId, Guid messageId, CancellationToken ct) =>
        db.SpvMessages.AnyAsync(m => m.Id == messageId && m.PfaRegistrationId == pfaId, ct);
}

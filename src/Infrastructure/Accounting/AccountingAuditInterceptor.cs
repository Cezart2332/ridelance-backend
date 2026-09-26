using System.Text.Json;
using Application.Abstractions.Authentication;
using Application.Accounting;
using Domain.Accounting;
using Infrastructure.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.Accounting;

/// <summary>
/// Auditul automat al înregistrărilor fiscale și contabile (spec contabilitate §0 pct. 7, B5): la
/// fiecare salvare, orice <see cref="IAccountingRecord"/> creat sau modificat lasă o intrare în
/// <see cref="AuditLog"/>, cu valorile vechi și noi ale câmpurilor schimbate.
/// </summary>
/// <remarks>
/// O intrare scrisă explicit de serviciu (cu acțiunea și motivul ei) pentru aceeași entitate, în
/// aceeași salvare, are întâietate: interceptorul nu o dublează. Nu se auditează jurnalul însuși,
/// cursurile BNR importate, starea de pre-check (recalculată) și liniile declarației (parte din
/// versiune, auditată ca întreg). Coloanele JSON mari (snapshot, istoric, text PDF) nu intră în audit.
/// </remarks>
internal sealed class AccountingAuditInterceptor(IUserContext userContext) : SaveChangesInterceptor
{
    private static readonly HashSet<Type> Skipped =
    [
        typeof(AuditLog),
        typeof(ExchangeRate),
        typeof(PfaMonthCheck),
        typeof(DeclarationLine),
        typeof(Declaration),
    ];

    private static readonly HashSet<string> SkippedProperties =
    [
        nameof(DeclarationVersion.SnapshotJson),
        nameof(DeclarationVersion.ValidationResultJson),
        nameof(DeclarationVersion.StatusHistoryJson),
        nameof(PlatformDocument.PdfText),
        nameof(DocumentExtraction.SourceSnippetsJson),
        nameof(DocumentExtraction.ChecksResultJson),
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAudit(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAudit(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAudit(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Interceptorul rulează înaintea detectării automate a modificărilor.
        context.ChangeTracker.DetectChanges();
        List<EntityEntry> entries = [.. context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is IAccountingRecord &&
                            entry.State is EntityState.Added or EntityState.Modified &&
                            !Skipped.Contains(entry.Metadata.ClrType))];
        if (entries.Count == 0)
        {
            return;
        }

        var explicitAudits = context.ChangeTracker.Entries<AuditLog>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => (entry.Entity.Entity, entry.Entity.EntityId))
            .ToHashSet();
        Guid? userId = CurrentUser();

        foreach (EntityEntry entry in entries)
        {
            string entity = entry.Metadata.ClrType.Name;
            string entityId = entry.Metadata.FindProperty("Id") is null ? string.Empty : entry.Property("Id").CurrentValue?.ToString() ?? string.Empty;
            if (explicitAudits.Contains((entity, entityId)))
            {
                continue;
            }

            List<PropertyEntry> properties = [.. entry.Properties.Where(p => !p.Metadata.IsShadowProperty() && !SkippedProperties.Contains(p.Metadata.Name))];
            Dictionary<string, object?>? before = null;
            Dictionary<string, object?> after;
            if (entry.State == EntityState.Added)
            {
                after = properties.ToDictionary(Key, p => p.CurrentValue);
            }
            else
            {
                List<PropertyEntry> changed = [.. properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))];
                if (changed.Count == 0)
                {
                    continue;
                }

                before = changed.ToDictionary(Key, p => p.OriginalValue);
                after = changed.ToDictionary(Key, p => p.CurrentValue);
            }

            context.Set<AuditLog>().Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = PfaOf(context, entry),
                Entity = entity,
                EntityId = entityId,
                Action = entry.State == EntityState.Added ? "CREATE" : "UPDATE",
                BeforeJson = before is null ? null : AccountingJson.Serialize(before),
                AfterJson = AccountingJson.Serialize(after),
                UserId = userId,
                AtUtc = DateTime.UtcNow,
            });
        }
    }

    /// <summary>Numele câmpului în forma API-ului (camelCase), ca intrările scrise explicit.</summary>
    private static string Key(PropertyEntry property) => JsonNamingPolicy.CamelCase.ConvertName(property.Metadata.Name);

    /// <summary>PFA-ul entității: coloana proprie sau, pentru o versiune de declarație, al declarației.</summary>
    private static Guid? PfaOf(DbContext context, EntityEntry entry)
    {
        if (entry.Metadata.FindProperty(nameof(AuditLog.PfaRegistrationId)) is not null)
        {
            return entry.Property(nameof(AuditLog.PfaRegistrationId)).CurrentValue as Guid?;
        }

        if (entry.Entity is DeclarationVersion version)
        {
            return version.Declaration?.PfaRegistrationId ??
                   context.ChangeTracker.Entries<Declaration>().FirstOrDefault(d => d.Entity.Id == version.DeclarationId)?.Entity.PfaRegistrationId;
        }

        return null;
    }

    /// <summary>Utilizatorul cererii; <c>null</c> în joburi și importuri, unde nu există unul.</summary>
    private Guid? CurrentUser()
    {
        try
        {
            return userContext.UserId;
        }
        catch (UserContextUnavailableException)
        {
            return null;
        }
    }
}

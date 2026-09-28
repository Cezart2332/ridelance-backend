using System.Text.Json.Serialization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Months;
using Domain.Accounting;
using Domain.Banking;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Pfas;

/// <summary>Unde e clientul în colaborare: activ în contabilitate, încă în onboarding sau inactiv.</summary>
[JsonConverter(typeof(UpperSnakeCaseEnumConverter<ClientStage>))]
public enum ClientStage
{
    Active = 0,
    Onboarding = 1,
    Inactive = 2,
}

/// <summary>
/// Un rând din „Clienți PFA” și „De făcut azi”: statusul lunii, cele trei declarații, banca și
/// mesajele necitite ale clientului, dintr-o singură cerere.
/// </summary>
/// <param name="MonthStatus"><c>null</c> pentru clienții încă în onboarding (nu au lună contabilă).</param>
/// <param name="Reason">Primul motiv pentru care luna nu e gata.</param>
/// <param name="BankStatus">
/// Starea conexiunii open banking (<c>LINKED</c>, <c>PENDING</c>, <c>EXPIRED</c>…); <c>null</c> = banca nu a fost
/// conectată niciodată. Text, ca celelalte stări ale modulului; enum-ul din Banking rămâne neatins.
/// </param>
public sealed record ClientWorkspaceRow(
    Guid PfaId,
    Guid UserId,
    string Name,
    string Cui,
    string Email,
    ClientStage Stage,
    PfaMonthStatus? MonthStatus,
    string? Reason,
    IReadOnlyDictionary<DeclarationType, DeclarationCell> Declarations,
    string? BankStatus,
    int UnreadMessages);

/// <summary>
/// <c>GET /accounting/clients?period=</c> — portofoliul: contabilul își vede clienții alocați,
/// adminul pe toți. Luna vine din aceeași privire de ansamblu ca pagina „Declarații”.
/// </summary>
public sealed record ListClientWorkspaceQuery(string Period) : IQuery<IReadOnlyList<ClientWorkspaceRow>>;

internal sealed class ListClientWorkspaceQueryHandler(IApplicationDbContext db, IUserContext userContext, IOptions<AccountingOptions> options)
    : IQueryHandler<ListClientWorkspaceQuery, IReadOnlyList<ClientWorkspaceRow>>
{
    public async Task<Result<IReadOnlyList<ClientWorkspaceRow>>> Handle(ListClientWorkspaceQuery query, CancellationToken cancellationToken)
    {
        Result<PeriodOverview> overview = await new GetPeriodOverviewQueryHandler(db, options)
            .Handle(new GetPeriodOverviewQuery(query.Period), cancellationToken);
        if (overview.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ClientWorkspaceRow>>(overview.Error);
        }

        Guid me = userContext.UserId;
        bool contabil = await db.Users.AnyAsync(u => u.Id == me && u.Role == UserRole.Contabil, cancellationToken);
        var portfolio = await db.PfaRegistrations.AsNoTracking()
            .Where(p => p.User.DeletedAtUtc == null && (!contabil || p.AssignedContabilId == me))
            .Select(p => new
            {
                p.Id,
                p.UserId,
                p.LegalName,
                p.HolderName,
                p.FullName,
                p.Cui,
                p.User.FirstName,
                p.User.LastName,
                p.User.Email,
                p.OnboardingCompletedAtUtc,
            })
            .ToListAsync(cancellationToken);

        List<Guid> ids = [.. portfolio.Select(p => p.Id)];
        List<Guid> users = [.. portfolio.Select(p => p.UserId)];
        var months = overview.Value.Rows.Where(row => ids.Contains(row.PfaId)).ToDictionary(row => row.PfaId);
        HashSet<Guid> engaged = [.. await db.PfaAccountingEngagements.AsNoTracking()
            .Where(e => ids.Contains(e.PfaRegistrationId))
            .Select(e => e.PfaRegistrationId)
            .ToListAsync(cancellationToken)];
        HashSet<Guid> inactive = [.. (await db.PfaAccountingEngagements.AsNoTracking()
                .Where(e => ids.Contains(e.PfaRegistrationId))
                .ToListAsync(cancellationToken))
            .GroupBy(e => e.PfaRegistrationId)
            .Where(group => group.OrderByDescending(e => e.StartDate).First().Status == EngagementStatus.Inactive)
            .Select(group => group.Key)];

        var banks = (await db.BankConnections.AsNoTracking()
                .Where(b => users.Contains(b.UserId))
                .Select(b => new { b.UserId, b.Status, b.CreatedAtUtc })
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.UserId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(b => b.CreatedAtUtc).First().Status);

        var unread = (await db.ChatMessages.AsNoTracking()
                .Where(m => !m.IsRead &&
                            m.ChatRoom.ProfessionalUserId == me &&
                            m.SenderId == m.ChatRoom.ClientUserId &&
                            users.Contains(m.ChatRoom.ClientUserId))
                .Select(m => m.ChatRoom.ClientUserId)
                .ToListAsync(cancellationToken))
            .GroupBy(user => user)
            .ToDictionary(group => group.Key, group => group.Count());

        List<ClientWorkspaceRow> rows = [.. portfolio
            .Select(p =>
            {
                OverviewRow? month = months.GetValueOrDefault(p.Id);
                bool started = month is not null || engaged.Contains(p.Id) || p.OnboardingCompletedAtUtc is not null;
                ClientStage stage = started ? ClientStage.Active : ClientStage.Onboarding;
                if (inactive.Contains(p.Id))
                {
                    stage = ClientStage.Inactive;
                }

                return new ClientWorkspaceRow(
                    p.Id,
                    p.UserId,
                    month?.PfaName ?? PfaNames.Of(p.LegalName, p.HolderName, p.FullName, p.FirstName, p.LastName),
                    p.Cui ?? string.Empty,
                    p.Email,
                    stage,
                    month?.Status,
                    month is { BlockingReasons.Count: > 0 } ? month.BlockingReasons[0] : null,
                    month?.Declarations ?? new Dictionary<DeclarationType, DeclarationCell>(),
                    banks.TryGetValue(p.UserId, out BankConnectionStatus bank) ? bank.ToString().ToUpperInvariant() : null,
                    unread.GetValueOrDefault(p.UserId));
            })
            .OrderBy(row => row.Name, StringComparer.Create(RegisterCulture, ignoreCase: true))];
        return rows;
    }

    private static readonly System.Globalization.CultureInfo RegisterCulture = System.Globalization.CultureInfo.GetCultureInfo("ro-RO");
}

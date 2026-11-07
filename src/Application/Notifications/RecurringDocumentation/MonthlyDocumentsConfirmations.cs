using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting;
using Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Notifications.RecurringDocumentation;

/// <summary>„Nu am bonuri de încărcat”: PFA-ul închide luna din partea lui.</summary>
public sealed record ConfirmMonthlyDocumentsCommand(int Year, int Month) : ICommand<string>;

/// <summary>Lunile confirmate în ultimul an, ca „2026-10”.</summary>
public sealed record GetMonthlyDocumentsConfirmationsQuery : IQuery<IReadOnlyList<string>>;

internal sealed class ConfirmMonthlyDocumentsCommandHandler(IApplicationDbContext context, IUserContext userContext)
    : ICommandHandler<ConfirmMonthlyDocumentsCommand, string>
{
    public async Task<Result<string>> Handle(ConfirmMonthlyDocumentsCommand command, CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;
        (int requestedYear, int requestedMonth) = AccountingPeriod.RequestedMonthUtc(nowUtc);
        int asked = command.Year * 12 + command.Month;
        int current = requestedYear * 12 + requestedMonth;
        // Luna cerută acum sau una din ultimul an: nu se confirmă luni viitoare.
        if (command.Month is < 1 or > 12 || asked > current || asked < current - 12)
        {
            return Result.Failure<string>(Error.Problem("MonthlyDocuments.InvalidMonth", "Luna nu poate fi confirmată."));
        }

        Guid userId = userContext.UserId;
        string monthKey = AccountingPeriod.MonthKey(command.Year, command.Month);
        bool exists = await context.MonthlyDocumentsConfirmations
            .AnyAsync(c => c.UserId == userId && c.Year == command.Year && c.Month == command.Month, cancellationToken);
        if (!exists)
        {
            context.MonthlyDocumentsConfirmations.Add(new MonthlyDocumentsConfirmation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Year = command.Year,
                Month = command.Month,
                ConfirmedAtUtc = nowUtc,
            });
        }

        // Cererile lunii nu mai au ce cere: le marcăm citite.
        List<Notification> requests = await context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead && n.SectionKey == monthKey &&
                        (n.Type == NotificationTypes.RecurringDocumentation || n.Type == NotificationTypes.RecurringDocumentationReminder))
            .ToListAsync(cancellationToken);
        foreach (Notification request in requests)
        {
            request.IsRead = true;
        }

        await context.SaveChangesAsync(cancellationToken);
        return monthKey;
    }
}

internal sealed class GetMonthlyDocumentsConfirmationsQueryHandler(IApplicationDbContext context, IUserContext userContext)
    : IQueryHandler<GetMonthlyDocumentsConfirmationsQuery, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> Handle(GetMonthlyDocumentsConfirmationsQuery query, CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;
        int fromYear = DateTime.UtcNow.Year - 1;
        var months = await context.MonthlyDocumentsConfirmations
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.Year >= fromYear)
            .Select(c => new { c.Year, c.Month })
            .ToListAsync(cancellationToken);

        return months.Select(m => AccountingPeriod.MonthKey(m.Year, m.Month)).ToList();
    }
}

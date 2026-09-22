using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.FiscalProfiles;
using Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.FiscalProfiles;

/// <summary>Rularea zilnică: reamintiri săptămânale și sarcina de apel de la ziua 30.</summary>
public sealed record RunFiscalProfileRemindersCommand : ICommand<FiscalProfileRemindersRun>;

public sealed record FiscalProfileRemindersRun(int RemindersSent, int CallTasksCreated);

/// <summary>
/// Ce i se cuvine unui PFA într-o zi: reamintirea săptămânii (dacă n-a primit-o) și sarcina de
/// apel (după 30 de zile). Pur, ca regulile să se poată testa fără bază de date.
/// </summary>
public static class FiscalProfileReminderSchedule
{
    public const int CallTaskAfterDays = 30;

    /// <summary>Săptămâna de reamintire în care cade ziua: 1 de la ziua 7, 2 de la ziua 14 etc. 0 = încă nimic.</summary>
    public static int WeekIndex(DateOnly startOn, DateOnly today)
    {
        int days = today.DayNumber - startOn.DayNumber;
        return days < 7 ? 0 : days / 7;
    }

    public static bool CallTaskDue(DateOnly startOn, DateOnly today) =>
        today.DayNumber - startOn.DayNumber >= CallTaskAfterDays;

    /// <summary>
    /// De când se numără: accesul în RIDElance, dar nu înainte de începutul anului fiscal —
    /// profilul unui an nou pornește numărătoarea de la 1 ianuarie.
    /// </summary>
    public static DateOnly StartOn(DateOnly accessOn, int taxYear)
    {
        var yearStart = new DateOnly(taxYear, 1, 1);
        return accessOn > yearStart ? accessOn : yearStart;
    }
}

internal sealed class RunFiscalProfileRemindersCommandHandler(
    IApplicationDbContext context,
    FiscalProfileService service,
    ILogger<RunFiscalProfileRemindersCommandHandler> logger)
    : ICommandHandler<RunFiscalProfileRemindersCommand, FiscalProfileRemindersRun>
{
    public const string ReminderText =
        "Profilul tău fiscal nu este completat. Completează-l ca să vezi estimările de taxe.";

    public async Task<Result<FiscalProfileRemindersRun>> Handle(
        RunFiscalProfileRemindersCommand command,
        CancellationToken cancellationToken)
    {
        DateTime nowUtc = service.UtcNow;
        var today = DateOnly.FromDateTime(FiscalProfileService.ToRomania(nowUtc));
        int year = today.Year;

        // PFA-urile înrolate, cu cont activ. Statusul profilului se citește acum, la trimitere:
        // cine a completat între timp nu mai primește nimic.
        var candidates = await context.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.OnboardingCompletedAtUtc != null && p.User.DeletedAtUtc == null)
            .Select(p => new
            {
                p.Id,
                p.UserId,
                AccessAtUtc = p.OnboardingCompletedAtUtc!.Value,
                Status = context.PfaTaxProfiles
                    .Where(t => t.PfaRegistrationId == p.Id && t.TaxYear == year)
                    .Select(t => (PfaTaxProfileStatus?)t.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        int reminders = 0;
        int tasks = 0;

        foreach (var pfa in candidates)
        {
            if (pfa.Status == PfaTaxProfileStatus.Completed)
            {
                continue;
            }

            var accessOn = DateOnly.FromDateTime(FiscalProfileService.ToRomania(pfa.AccessAtUtc));
            DateOnly startOn = FiscalProfileReminderSchedule.StartOn(accessOn, year);
            int week = FiscalProfileReminderSchedule.WeekIndex(startOn, today);

            if (week > 0 && !await context.FiscalProfileReminders.AnyAsync(
                    r => r.PfaRegistrationId == pfa.Id && r.TaxYear == year && r.WeekIndex == week,
                    cancellationToken))
            {
                context.FiscalProfileReminders.Add(new FiscalProfileReminder
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = pfa.Id,
                    TaxYear = year,
                    WeekIndex = week,
                    ScheduledAtUtc = nowUtc,
                    DeliveredAtUtc = nowUtc,
                });

                // Numai în platformă: fără email, fără SMS, fără push.
                context.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = pfa.UserId,
                    Text = ReminderText,
                    Type = NotificationTypes.FiscalProfile,
                    DedupeKey = $"fiscal-profile:{pfa.Id}:{year}:w{week}",
                    CreatedAtUtc = nowUtc,
                });
                reminders++;
            }

            if (FiscalProfileReminderSchedule.CallTaskDue(startOn, today) && !await context.AdminCallTasks.AnyAsync(
                    t => t.PfaRegistrationId == pfa.Id && t.TaxYear == year && t.Reason == AdminCallTask.ProfileIncomplete30Days,
                    cancellationToken))
            {
                context.AdminCallTasks.Add(new AdminCallTask
                {
                    Id = Guid.NewGuid(),
                    PfaRegistrationId = pfa.Id,
                    TaxYear = year,
                    Reason = AdminCallTask.ProfileIncomplete30Days,
                    State = AdminCallTaskState.Open,
                    CreatedAtUtc = nowUtc,
                });
                tasks++;
            }
        }

        if (reminders + tasks > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Profil fiscal: {Reminders} reamintiri trimise, {Tasks} sarcini de apel create.", reminders, tasks);

        return new FiscalProfileRemindersRun(reminders, tasks);
    }
}

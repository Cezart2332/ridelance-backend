using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.MonthlyIncome;

public sealed record ProcessPfaMonthlyIncomeCommand(
    Guid PfaRegistrationId,
    int Year,
    int Month,
    bool IsProcessed) : ICommand<PfaMonthlyIncomeResponse>;

internal sealed class ProcessPfaMonthlyIncomeCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IWebPushService webPushService,
    IConfiguration configuration)
    : ICommandHandler<ProcessPfaMonthlyIncomeCommand, PfaMonthlyIncomeResponse>
{
    public async Task<Result<PfaMonthlyIncomeResponse>> Handle(
        ProcessPfaMonthlyIncomeCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .Include(p => p.User)
            .ThenInclude(u => u.PushSubscriptions)
            .SingleOrDefaultAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<PfaMonthlyIncomeResponse>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<PfaMonthlyIncomeResponse>(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        bool canEdit = caller.Role is UserRole.Admin
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId;

        if (!canEdit)
        {
            return Result.Failure<PfaMonthlyIncomeResponse>(
                Error.Failure("Pfa.AccessDenied", "Nu ai permisiunea de a modifica statusul procesării."));
        }

        PfaMonthlyIncome? income = await context.PfaMonthlyIncomes
            .SingleOrDefaultAsync(
                i => i.PfaRegistrationId == command.PfaRegistrationId
                    && i.Year == command.Year
                    && i.Month == command.Month,
                cancellationToken);

        if (income is null)
        {
            income = new PfaMonthlyIncome
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaRegistrationId,
                Year = command.Year,
                Month = command.Month,
                VenitCash = 0,
                VenitCard = 0,
                VenitBolt = 0,
                VenitUber = 0,
                TaxeEstimate = 0
            };
            context.PfaMonthlyIncomes.Add(income);
        }

        bool wasProcessed = income.IsProcessed;

        income.IsProcessed = command.IsProcessed;
        income.UpdatedAtUtc = DateTime.UtcNow;
        income.UpdatedByUserId = userContext.UserId;

        if (command.IsProcessed)
        {
            income.ProcessedAtUtc = DateTime.UtcNow;
            income.ProcessedByUserId = userContext.UserId;
            income.ProcessedByUser = caller;
        }
        else
        {
            income.ProcessedAtUtc = null;
            income.ProcessedByUserId = null;
            income.ProcessedByUser = null;
        }

        if (command.IsProcessed && !wasProcessed)
        {
            string monthLabel = new DateTime(command.Year, command.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("MMMM yyyy", new CultureInfo("ro-RO"));
            if (!string.IsNullOrEmpty(monthLabel))
            {
                monthLabel = char.ToUpper(monthLabel[0], CultureInfo.InvariantCulture) + monthLabel[1..];
            }

            string roleName = caller.Role is UserRole.Admin ? "Admin" : "Contabil";
            string clientText = $"Luna {monthLabel} a fost marcată ca procesată de {roleName} {caller.FirstName} {caller.LastName}.";
            string adminText = $"Luna {monthLabel} pentru clientul {pfa.User.FirstName} {pfa.User.LastName} a fost marcată ca procesată de {roleName} {caller.FirstName} {caller.LastName}.";

            var activityLog = new PfaActivityLog
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaRegistrationId,
                ActivityType = "MonthProcessed",
                Description = $"Luna {monthLabel} a fost marcată ca procesată de {roleName} {caller.FirstName} {caller.LastName}.",
                CreatedAtUtc = DateTime.UtcNow,
                PerformedByUserId = userContext.UserId
            };
            context.PfaActivityLogs.Add(activityLog);

            var clientNotification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = pfa.UserId,
                Text = clientText,
                Type = NotificationTypes.MonthProcessed,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Notifications.Add(clientNotification);

            List<User> admins = await context.Users
                .Include(u => u.PushSubscriptions)
                .Where(u => u.Role == UserRole.Admin)
                .ToListAsync(cancellationToken);

            foreach (User admin in admins)
            {
                var adminNotification = new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = admin.Id,
                    Text = adminText,
                    Type = NotificationTypes.MonthProcessed,
                    IsRead = false,
                    CreatedAtUtc = DateTime.UtcNow
                };
                context.Notifications.Add(adminNotification);
            }

            await context.SaveChangesAsync(cancellationToken);

            Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
            string clientRelativePath = "/dashboard";
            string clientDeepLink = appBaseUri is null ? clientRelativePath : new Uri(appBaseUri, clientRelativePath).ToString();

            foreach (PushSubscription sub in pfa.User.PushSubscriptions)
            {
                try
                {
                    await webPushService.SendPushNotificationAsync(sub, "Lună procesată", clientText, clientDeepLink, cancellationToken);
                }
                catch
                {
                    // Ignore push sending failures
                }
            }

            string adminRelativePath = "/contabil/dashboard";
            string adminDeepLink = appBaseUri is null ? adminRelativePath : new Uri(appBaseUri, adminRelativePath).ToString();

            foreach (User admin in admins)
            {
                foreach (PushSubscription sub in admin.PushSubscriptions)
                {
                    try
                    {
                        await webPushService.SendPushNotificationAsync(sub, "Lună procesată (Admin)", adminText, adminDeepLink, cancellationToken);
                    }
                    catch
                    {
                        // Ignore push sending failures
                    }
                }
            }
        }
        else
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return GetPfaMonthlyIncomeQueryHandler.Map(income);
    }
}

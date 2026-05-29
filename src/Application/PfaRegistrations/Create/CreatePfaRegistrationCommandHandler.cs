using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Domain.Notifications;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.Create;

internal sealed class CreatePfaRegistrationCommandHandler(
    IApplicationDbContext context,
    IWebPushService webPushService,
    IConfiguration configuration)
    : ICommandHandler<CreatePfaRegistrationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreatePfaRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? existing = await context.PfaRegistrations
            .Include(r => r.Documents)
            .FirstOrDefaultAsync(r => r.UserId == command.UserId, cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == PfaRegistrationStatus.Rejected)
            {
                foreach (Document doc in existing.Documents)
                {
                    context.Documents.Remove(doc);
                }
                context.PfaRegistrations.Remove(existing);
                await context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                return Result.Failure<Guid>(PfaRegistrationErrors.AlreadyExists);
            }
        }

        // Load balancing: assign the PFA to a Contabil
        var contabili = await context.Users
            .Where(u => u.Role == UserRole.Contabil)
            .Select(u => new
            {
                User = u,
                AssignedCount = context.PfaRegistrations.Count(p => p.AssignedContabilId == u.Id)
            })
            .ToListAsync(cancellationToken);

        User? assignedContabil = contabili
            .OrderBy(c => c.AssignedCount)
            .ThenBy(c => c.User.CreatedAtUtc)
            .Select(c => c.User)
            .FirstOrDefault();

        var registration = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            RegistrationType = command.RegistrationType,
            FullName = command.FullName,
            Phone = command.Phone,
            ContractDuration = command.ContractDuration,
            Street = command.Street,
            Number = command.Number,
            City = command.City,
            County = command.County,
            IsOwner = command.IsOwner,
            Status = PfaRegistrationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            AssignedContabilId = assignedContabil?.Id
        };

        context.PfaRegistrations.Add(registration);

        await context.SaveChangesAsync(cancellationToken);

        // Send Onboarding Started Notifications
        User? clientUser = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        
        string clientName = clientUser != null 
            ? $"{clientUser.FirstName} {clientUser.LastName}" 
            : (command.FullName ?? "Client Nou");

        List<Guid> recipientIds = [];
        if (assignedContabil != null)
        {
            recipientIds.Add(assignedContabil.Id);
        }

        List<User> admins = await context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Admin)
            .ToListAsync(cancellationToken);

        foreach (User admin in admins)
        {
            if (!recipientIds.Contains(admin.Id))
            {
                recipientIds.Add(admin.Id);
            }
        }

        string notificationText = $"{clientName} a început înrolarea.";

        foreach (Guid recipientId in recipientIds)
        {
            var notification = new Notification
            {
                Id = Guid.NewGuid(),
                UserId = recipientId,
                Text = notificationText,
                Type = NotificationTypes.OnboardingStarted,
                IsRead = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            context.Notifications.Add(notification);
        }

        await context.SaveChangesAsync(cancellationToken);

        // Send push notifications
        List<PushSubscription> subscriptions = await context.PushSubscriptions
            .Where(s => recipientIds.Contains(s.UserId))
            .ToListAsync(cancellationToken);

        string pushTitle = "Înrolare nouă";
        string pushBody = $"{clientName} a început înrolarea.";

        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
        
        foreach (PushSubscription sub in subscriptions)
        {
            User? recipient = await context.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == sub.UserId, cancellationToken);

            string relativePath = recipient?.Role == UserRole.Admin ? "/admin/dashboard" : "/contabil/dashboard";
            string deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();

            try
            {
                await webPushService.SendPushNotificationAsync(sub, pushTitle, pushBody, deepLink, cancellationToken);
            }
            catch
            {
                // Ignore push sending failures
            }
        }

        return registration.Id;
    }
}

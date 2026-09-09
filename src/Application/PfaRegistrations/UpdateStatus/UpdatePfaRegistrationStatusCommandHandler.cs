using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Notifications;
using Application.PfaRegistrations.Onboarding;
using Domain.Documents;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.PfaRegistrations.UpdateStatus;

internal sealed class UpdatePfaRegistrationStatusCommandHandler(
    IApplicationDbContext context,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    IWebPushService webPushService,
    IConfiguration configuration)
    : ICommandHandler<UpdatePfaRegistrationStatusCommand>
{
    public async Task<Result> Handle(
        UpdatePfaRegistrationStatusCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.Documents)
            .Include(r => r.OnboardingSections)
            .Include(r => r.User)
                .ThenInclude(u => u.PushSubscriptions)
            .SingleOrDefaultAsync(r => r.Id == command.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Result.Failure(PfaRegistrationErrors.NotFound(command.RegistrationId));
        }

        if (registration.Status is not PfaRegistrationStatus.Pending)
        {
            return Result.Failure(PfaRegistrationErrors.AlreadyReviewed);
        }

        if (command.NewStatus == PfaRegistrationStatus.Approved)
        {
            // Aprobarea închide pasul PFA, iar un pas închis nu mai acceptă scrieri (RL-01). Pe
            // ramura „Am PFA" asta însemna că un dosar aprobat înainte ca șoferul să-și fi încărcat
            // certificatul constatator îl lăsa fără nicio cale de a-l mai încărca: ecranul dispărea,
            // uploadul era refuzat, iar dosarul rămânea incomplet la ARR. Deci nu se aprobă înainte.
            List<Document> certificates = await context.Documents
                .Where(d => d.UserId == registration.UserId
                    && (d.Category == DocumentCategory.CertificatInregistrare
                        || d.Category == DocumentCategory.CertificatConstatator))
                .ToListAsync(cancellationToken);

            if (!OnboardingStepCatalog.PfaUserPartDone(registration, certificates))
            {
                return Result.Failure(Error.Failure(
                    "PfaRegistration.UserPartIncomplete",
                    "Șoferul nu a încărcat încă ambele certificate ONRC (înregistrare și constatator). "
                    + "Aprobarea acum i-ar închide pasul fără ele."));
            }

            if (string.IsNullOrWhiteSpace(command.Cui))
            {
                return Result.Failure(Error.Failure("PfaRegistration.CuiRequired", "CUI-ul este obligatoriu pentru aprobare."));
            }

            (bool isValid, string message) = CuiValidator.Validate(command.Cui);
            if (!isValid)
            {
                return Result.Failure(Error.Failure("PfaRegistration.InvalidCui", message));
            }

            // La „Am PFA" certificatul e deja încărcat de user în pasul 1, deci adminul nu mai
            // trebuie să-l urce încă o dată; îl cere doar când chiar lipsește (cazul „Nu am PFA").
            Document? document = command.DocumentId is not null
                ? await context.Documents
                    .SingleOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken)
                : certificates
                    .Where(d => d.Category == DocumentCategory.CertificatInregistrare
                        && d.Status != DocumentStatus.Rejected)
                    .OrderByDescending(d => d.UploadedAtUtc)
                    .FirstOrDefault();

            if (document == null)
            {
                return command.DocumentId is null
                    ? Result.Failure(Error.Failure("PfaRegistration.DocumentRequired", "Certificatul de înregistrare este obligatoriu pentru aprobare."))
                    : Result.Failure(Error.NotFound("Documents.NotFound", "Documentul specificat nu a fost găsit."));
            }

            // Link document and update its metadata
            document.PfaRegistrationId = registration.Id;
            document.Category = DocumentCategory.CertificatInregistrare;
            document.Status = DocumentStatus.Verified;
            
            registration.Cui = command.Cui;
        }

        registration.Status = command.NewStatus;
        registration.ReviewNote = command.ReviewNote;
        registration.ReviewedAtUtc = DateTime.UtcNow;
        registration.ReviewedByUserId = command.ReviewerUserId;

        // Aprobarea PFA = validarea secțiunii 1 de onboarding → deblochează secțiunea 2.
        if (command.NewStatus == PfaRegistrationStatus.Approved &&
            registration.OnboardingSections.All(s => s.SectionKey != OnboardingSectionKey.AutorizatieTransport))
        {
            context.OnboardingSectionApprovals.Add(new OnboardingSectionApproval
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
                SectionKey = OnboardingSectionKey.AutorizatieTransport,
                Status = OnboardingSectionStatus.InProgress,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        // Create in-app notification
        string text = command.NewStatus == PfaRegistrationStatus.Approved
            ? "Dosarul tău PFA a fost aprobat! CUI-ul a fost generat."
            : "Dosarul tău PFA a fost respins. Vezi mențiunile contabilului.";

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = registration.UserId,
            Text = text,
            Type = NotificationTypes.PfaStatusUpdate,
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Notifications.Add(notification);

        await context.SaveChangesAsync(cancellationToken);

        // Send styled MJML Email
        string mjml = command.NewStatus == PfaRegistrationStatus.Approved
            ? ClientRegistrationStatusEmail.BuildApprovedMjml(
                UserDisplayName.GreetingFor(registration.User), registration.Cui ?? "")
            : ClientRegistrationStatusEmail.BuildRejectedMjml(
                UserDisplayName.GreetingFor(registration.User), command.ReviewNote ?? "");

        string htmlBody = mjmlRenderer.Render(mjml);
        string subject = command.NewStatus == PfaRegistrationStatus.Approved
            ? ClientRegistrationStatusEmail.ApprovedSubject
            : ClientRegistrationStatusEmail.RejectedSubject;

        try
        {
            await emailService.SendEmailAsync(registration.User.Email, subject, htmlBody, cancellationToken);
        }
        catch
        {
            // Log/ignore email sending errors to prevent failing the status update
        }

        // Send web push notification
        string pushTitle = command.NewStatus == PfaRegistrationStatus.Approved ? "PFA Aprobat" : "Dosar PFA Neconform";
        string pushBody = command.NewStatus == PfaRegistrationStatus.Approved
            ? "PFA-ul tău a fost aprobat!"
            : "Dosarul tău necesită modificări.";

        Uri? appBaseUri = Uri.TryCreate(configuration["App:BaseUrl"], UriKind.Absolute, out Uri? parsedBase) ? parsedBase : null;
        string relativePath = "/onboarding";
        string deepLink = appBaseUri is null ? relativePath : new Uri(appBaseUri, relativePath).ToString();

        foreach (PushSubscription sub in registration.User.PushSubscriptions)
        {
            try
            {
                await webPushService.SendPushNotificationAsync(sub, pushTitle, pushBody, deepLink, cancellationToken);
            }
            catch
            {
                // Ignore push sending failures
            }
        }

        return Result.Success();
    }
}

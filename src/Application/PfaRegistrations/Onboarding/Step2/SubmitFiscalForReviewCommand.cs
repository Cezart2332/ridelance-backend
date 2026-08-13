using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Notifications;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>
/// Pasul fiscal — șoferul își declară partea terminată și trimite dosarul spre admin. De aici
/// încolo nu mai are ce face: verificarea contului și pachetul de semnături sunt ale noastre.
/// </summary>
public sealed record SubmitFiscalForReviewCommand(Guid UserId) : ICommand;

internal sealed class SubmitFiscalForReviewCommandHandler(
    IApplicationDbContext context,
    OnboardingStateService stateService)
    : ICommandHandler<SubmitFiscalForReviewCommand>
{
    public async Task<Result> Handle(SubmitFiscalForReviewCommand command, CancellationToken cancellationToken)
    {
        Result guard = await stateService.EnsureWritableAsync(
            command.UserId, OnboardingStepKey.Fiscal, cancellationToken);

        if (guard.IsFailure)
        {
            return Result.Failure(guard.Error);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.FiscalProfile)
            .Include(r => r.BankAccountDeclaration)
            .Include(r => r.OblioAccount)
            .Include(r => r.SignaturePacket)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure(Step2Errors.NoRegistration);
        }

        if (!OnboardingStepCatalog.FiscalUserPartComplete(registration))
        {
            return Result.Failure(Step2Errors.FiscalIncomplete);
        }

        DateTime nowUtc = DateTime.UtcNow;

        OnboardingSignaturePacket packet = registration.SignaturePacket ?? new OnboardingSignaturePacket
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };

        if (registration.SignaturePacket is null)
        {
            context.OnboardingSignaturePackets.Add(packet);
        }

        // Retrimitere după o respingere: motivul vechi dispare, altfel ar rămâne pe ecran ca o
        // observație încă valabilă.
        string fromStatus = packet.SubmittedForReviewAtUtc is null ? "in_progress" : "pending_admin";
        packet.SubmittedForReviewAtUtc = nowUtc;
        packet.RejectionReason = null;
        packet.Status = SignaturePacketStatus.Draft;
        packet.UpdatedAtUtc = nowUtc;

        context.OnboardingStepAudits.Add(new OnboardingStepAudit
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            StepKey = OnboardingStepCatalog.WireKeyOf(OnboardingStepKey.Fiscal),
            FromStatus = fromStatus,
            ToStatus = "pending_admin",
            PerformedByUserId = command.UserId,
            CreatedAtUtc = nowUtc,
        });

        // Adminii află că au ceva de făcut. Fără asta, dosarul ar aștepta până când se uită
        // cineva din proprie inițiativă în listă.
        List<Guid> adminIds = await context.Users
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (Guid adminId in adminIds)
        {
            context.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = adminId,
                Text = "Un dosar așteaptă pachetul de semnături (pasul „Fiscal, bancă & semnături”).",
                Type = NotificationTypes.OnboardingStepAwaitingAdmin,
                IsRead = false,
                CreatedAtUtc = nowUtc,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

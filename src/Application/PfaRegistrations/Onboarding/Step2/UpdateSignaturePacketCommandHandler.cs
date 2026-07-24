using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal sealed class UpdateSignaturePacketCommandHandler(IApplicationDbContext context)
    : ICommandHandler<UpdateSignaturePacketCommand>
{
    public async Task<Result> Handle(UpdateSignaturePacketCommand command, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.SignaturePacket)
            .SingleOrDefaultAsync(r => r.Id == command.RegistrationId, cancellationToken);

        if (registration is null)
        {
            return Result.Failure(Step2Errors.NoRegistration);
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

        packet.Provider = command.Provider;
        packet.ProviderReference = command.ProviderReference;
        packet.AdminNote = command.AdminNote;
        packet.UpdatedAtUtc = nowUtc;

        if (command.Status == SignaturePacketStatus.Sent && packet.SentAtUtc is null)
        {
            packet.SentAtUtc = nowUtc;
        }

        if (command.Status == SignaturePacketStatus.Completed)
        {
            packet.SignedAtUtc ??= nowUtc;
        }

        packet.Status = command.Status;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

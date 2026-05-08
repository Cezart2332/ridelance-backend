using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.UpdateStatus;

internal sealed class UpdatePfaRegistrationStatusCommandHandler(IApplicationDbContext context)
    : ICommandHandler<UpdatePfaRegistrationStatusCommand>
{
    public async Task<Result> Handle(
        UpdatePfaRegistrationStatusCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.Documents)
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
            if (string.IsNullOrWhiteSpace(command.Cui))
            {
                return Result.Failure(Error.Failure("PfaRegistration.CuiRequired", "CUI-ul este obligatoriu pentru aprobare."));
            }

            (bool isValid, string message) = CuiValidator.Validate(command.Cui);
            if (!isValid)
            {
                return Result.Failure(Error.Failure("PfaRegistration.InvalidCui", message));
            }

            if (command.DocumentId == null)
            {
                return Result.Failure(Error.Failure("PfaRegistration.DocumentRequired", "Certificatul de înregistrare este obligatoriu pentru aprobare."));
            }

            Domain.Documents.Document? document = await context.Documents
                .SingleOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken);

            if (document == null)
            {
                return Result.Failure(Error.NotFound("Documents.NotFound", "Documentul specificat nu a fost găsit."));
            }

            // Link document and update its metadata
            document.PfaRegistrationId = registration.Id;
            document.Category = Domain.Documents.DocumentCategory.CertificatInregistrare;
            document.Status = Domain.Documents.DocumentStatus.Verified;
            
            registration.Cui = command.Cui;
        }

        registration.Status = command.NewStatus;
        registration.ReviewNote = command.ReviewNote;
        registration.ReviewedAtUtc = DateTime.UtcNow;
        registration.ReviewedByUserId = command.ReviewerUserId;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

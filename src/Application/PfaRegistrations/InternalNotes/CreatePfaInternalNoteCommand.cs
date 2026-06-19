using System;
using System.Threading;
using System.Threading.Tasks;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.InternalNotes;

public sealed record CreatePfaInternalNoteCommand(
    Guid PfaRegistrationId,
    int Year,
    int Month,
    string Content) : ICommand<PfaInternalNoteResponse>;

internal sealed class CreatePfaInternalNoteCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<CreatePfaInternalNoteCommand, PfaInternalNoteResponse>
{
    public async Task<Result<PfaInternalNoteResponse>> Handle(
        CreatePfaInternalNoteCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .SingleOrDefaultAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<PfaInternalNoteResponse>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        User? caller = await context.Users
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure<PfaInternalNoteResponse>(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        if (caller.Role is UserRole.Client)
        {
            return Result.Failure<PfaInternalNoteResponse>(
                Error.Failure("Pfa.AccessDenied", "Nu ai permisiunea de a crea note interne."));
        }

        bool hasAccess = caller.Role is UserRole.Admin
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId;

        if (!hasAccess)
        {
            return Result.Failure<PfaInternalNoteResponse>(
                Error.Failure("Pfa.AccessDenied", "Nu ai permisiunea de a adăuga note pentru acest client."));
        }

        if (string.IsNullOrWhiteSpace(command.Content))
        {
            return Result.Failure<PfaInternalNoteResponse>(
                Error.Problem("Note.ContentEmpty", "Conținutul notei nu poate fi gol."));
        }

        var note = new PfaInternalNote
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaRegistrationId,
            Year = command.Year,
            Month = command.Month,
            Content = command.Content.Trim(),
            CreatedByUserId = userContext.UserId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUser = caller
        };

        context.PfaInternalNotes.Add(note);
        await context.SaveChangesAsync(cancellationToken);

        return new PfaInternalNoteResponse(
            note.Id,
            note.PfaRegistrationId,
            note.Year,
            note.Month,
            note.Content,
            note.CreatedByUserId,
            $"{caller.FirstName} {caller.LastName}",
            note.CreatedAtUtc,
            note.UpdatedAtUtc);
    }
}

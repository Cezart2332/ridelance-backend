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

public sealed record DeletePfaInternalNoteCommand(Guid NoteId) : ICommand;

internal sealed class DeletePfaInternalNoteCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<DeletePfaInternalNoteCommand>
{
    public async Task<Result> Handle(
        DeletePfaInternalNoteCommand command,
        CancellationToken cancellationToken)
    {
        PfaInternalNote? note = await context.PfaInternalNotes
            .SingleOrDefaultAsync(n => n.Id == command.NoteId, cancellationToken);

        if (note is null)
        {
            return Result.Failure(
                Error.NotFound("Note.NotFound", "Nota internă nu a fost găsită."));
        }

        PfaRegistration? pfa = await context.PfaRegistrations
            .SingleOrDefaultAsync(p => p.Id == note.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        User? caller = await context.Users
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null)
        {
            return Result.Failure(
                Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        if (caller.Role is UserRole.Client)
        {
            return Result.Failure(
                Error.Failure("Pfa.AccessDenied", "Nu ai permisiunea de a șterge note interne."));
        }

        bool hasAccess = caller.Role is UserRole.Admin
            || note.CreatedByUserId == userContext.UserId
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId;

        if (!hasAccess)
        {
            return Result.Failure(
                Error.Failure("Pfa.AccessDenied", "Nu ai permisiunea de a șterge această notă."));
        }

        context.PfaInternalNotes.Remove(note);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

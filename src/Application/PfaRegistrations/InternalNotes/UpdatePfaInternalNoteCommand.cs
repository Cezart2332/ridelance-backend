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

public sealed record UpdatePfaInternalNoteCommand(
    Guid NoteId,
    string Content) : ICommand<PfaInternalNoteResponse>;

internal sealed class UpdatePfaInternalNoteCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpdatePfaInternalNoteCommand, PfaInternalNoteResponse>
{
    public async Task<Result<PfaInternalNoteResponse>> Handle(
        UpdatePfaInternalNoteCommand command,
        CancellationToken cancellationToken)
    {
        PfaInternalNote? note = await context.PfaInternalNotes
            .Include(n => n.CreatedByUser)
            .SingleOrDefaultAsync(n => n.Id == command.NoteId, cancellationToken);

        if (note is null)
        {
            return Result.Failure<PfaInternalNoteResponse>(
                Error.NotFound("Note.NotFound", "Nota internă nu a fost găsită."));
        }

        PfaRegistration? pfa = await context.PfaRegistrations
            .SingleOrDefaultAsync(p => p.Id == note.PfaRegistrationId, cancellationToken);

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
                Error.Failure("Pfa.AccessDenied", "Nu ai permisiunea de a edita note interne."));
        }

        bool hasAccess = caller.Role is UserRole.Admin
            || note.CreatedByUserId == userContext.UserId
            || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userContext.UserId;

        if (!hasAccess)
        {
            return Result.Failure<PfaInternalNoteResponse>(
                Error.Failure("Pfa.AccessDenied", "Nu ai permisiunea de a modifica această notă."));
        }

        if (string.IsNullOrWhiteSpace(command.Content))
        {
            return Result.Failure<PfaInternalNoteResponse>(
                Error.Problem("Note.ContentEmpty", "Conținutul notei nu poate fi gol."));
        }

        note.Content = command.Content.Trim();
        note.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return new PfaInternalNoteResponse(
            note.Id,
            note.PfaRegistrationId,
            note.Year,
            note.Month,
            note.Content,
            note.CreatedByUserId,
            $"{note.CreatedByUser.FirstName} {note.CreatedByUser.LastName}",
            note.CreatedAtUtc,
            note.UpdatedAtUtc);
    }
}

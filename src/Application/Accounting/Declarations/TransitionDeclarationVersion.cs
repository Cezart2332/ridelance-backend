using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Accounting.Contracts;
using Application.Accounting.Documents;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary><c>POST /accounting/declaration-versions/{id}/transitions</c></summary>
public sealed record TransitionDeclarationVersionCommand(Guid VersionId, DeclarationAction Action, string? Note) : ICommand<DeclarationVersionDto>;

/// <summary>
/// Tranzițiile unei versiuni. B4: <c>VALIDATE</c> (validarea pe 3 niveluri, sincron). Restul
/// acțiunilor (semnat, depus, respins, regenerare) vin cu mașina de stări din B5.
/// </summary>
internal sealed class TransitionDeclarationVersionCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    DeclarationValidator validator)
    : ICommandHandler<TransitionDeclarationVersionCommand, DeclarationVersionDto>
{
    public async Task<Result<DeclarationVersionDto>> Handle(TransitionDeclarationVersionCommand command, CancellationToken cancellationToken)
    {
        var version = await db.DeclarationVersions
            .AsNoTracking()
            .Where(v => v.Id == command.VersionId)
            .Select(v => new
            {
                v.Status,
                v.VersionNo,
                v.Declaration.PfaRegistrationId,
                v.Declaration.Period,
                Latest = v.Declaration.Versions.Max(other => other.VersionNo),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (version is null)
        {
            return Result.Failure<DeclarationVersionDto>(DeclarationErrors.VersionNotFound);
        }

        Result writable = await PlatformDocumentSupport.EnsureWritableAsync(db, version.PfaRegistrationId, version.Period, cancellationToken);
        if (writable.IsFailure)
        {
            return Result.Failure<DeclarationVersionDto>(writable.Error);
        }

        if (version.VersionNo != version.Latest)
        {
            return Result.Failure<DeclarationVersionDto>(DeclarationErrors.NotCurrentVersion);
        }

        if (command.Action != DeclarationAction.Validate)
        {
            return Result.Failure<DeclarationVersionDto>(DeclarationErrors.InvalidTransition(version.Status, command.Action));
        }

        Result<ValidationRun> run = await validator.ValidateAsync(command.VersionId, userContext.UserId, cancellationToken);
        if (run.IsFailure)
        {
            return Result.Failure<DeclarationVersionDto>(run.Error);
        }

        return (await DeclarationDtos.VersionAsync(db, command.VersionId, cancellationToken))!;
    }
}

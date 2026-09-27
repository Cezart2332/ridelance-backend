using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Documents;

/// <summary>
/// <c>DELETE /accounting/platform-documents/{id}</c> — doar ADMIN (permisiune separată). Ștergere
/// logică a unui document încărcat greșit (alt PFA, altă lună, fișier greșit): dispare din listă, din
/// verificări și din calcul; rândul, fișierul și auditul rămân.
/// <para>
/// Nu se șterge un document inclus într-o declarație (se corectează și se regenerează, sau prin
/// rectificativă) și nici unul dintr-o perioadă închisă.
/// </para>
/// </summary>
public sealed record DeletePlatformDocumentCommand(Guid Id) : ICommand;

internal sealed class DeletePlatformDocumentCommandHandler(IApplicationDbContext db, IUserContext userContext, IOptions<AccountingOptions> options)
    : ICommandHandler<DeletePlatformDocumentCommand>
{
    public async Task<Result> Handle(DeletePlatformDocumentCommand command, CancellationToken cancellationToken)
    {
        PlatformDocument? document = await db.PlatformDocuments.SingleOrDefaultAsync(d => d.Id == command.Id, cancellationToken);
        if (document is null)
        {
            return Result.Failure(AccountingErrors.DocumentNotFound);
        }

        bool periodClosed = await db.PfaAccountingPeriods.AnyAsync(
            p => p.PfaRegistrationId == document.PfaRegistrationId && p.Period == document.Period && p.Status == AccountingPeriodStatus.Closed,
            cancellationToken);
        if (periodClosed)
        {
            return Result.Failure(Error.Conflict("Accounting.DocumentLocked", $"Perioada {document.Period} e închisă: documentul nu se mai poate șterge."));
        }

        string? declaration = await db.DeclarationLines
            .Where(line => line.SourceDocumentId == document.Id && line.SupersededAtUtc == null)
            .Select(line => line.DeclarationVersion.Declaration.Type + " " + line.DeclarationVersion.Declaration.Period)
            .FirstOrDefaultAsync(cancellationToken);
        if (declaration is not null)
        {
            return Result.Failure(Error.Conflict(
                "Accounting.DocumentInDeclaration",
                $"Documentul e inclus în {declaration}: nu se poate șterge. Corectează-l și regenerează declarația, sau fă o rectificativă."));
        }

        document.DeletedAtUtc = DateTime.UtcNow;
        document.DeletedByUserId = userContext.UserId;
        AccountingAudit.Record(
            db, document.PfaRegistrationId, nameof(PlatformDocument), document.Id, "DELETE",
            new { document.Period, document.Platform, document.DocumentType, document.Status }, null, null, userContext.UserId);
        await db.SaveChangesAsync(cancellationToken);

        // Luna deja procesată își reface statusul: documentul lipsă se vede imediat.
        await Months.PreCheck.RefreshIfProcessedAsync(db, document.PfaRegistrationId, document.Period, options.Value, cancellationToken);
        return Result.Success();
    }
}

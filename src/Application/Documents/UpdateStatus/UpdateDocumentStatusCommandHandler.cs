using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.UpdateStatus;

internal sealed class UpdateDocumentStatusCommandHandler(IApplicationDbContext context)
    : ICommandHandler<UpdateDocumentStatusCommand>
{
    public async Task<Result> Handle(
        UpdateDocumentStatusCommand command,
        CancellationToken cancellationToken)
    {
        Document? document = await context.Documents
            .SingleOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure(DocumentErrors.NotFound(command.DocumentId));
        }

        User? user = await context.Users
            .SingleOrDefaultAsync(u => u.Id == command.RequestingUserId, cancellationToken);

        if (user is null || user.Role != UserRole.Admin && user.Role != UserRole.Contabil)
        {
            return Result.Failure(DocumentErrors.AccessDenied);
        }

        document.Status = command.Status;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

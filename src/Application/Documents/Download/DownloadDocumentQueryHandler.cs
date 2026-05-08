using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Documents;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.Download;

internal sealed class DownloadDocumentQueryHandler(
    IApplicationDbContext context,
    IFileEncryptionService fileEncryptionService)
    : IQueryHandler<DownloadDocumentQuery, DownloadDocumentResponse>
{
    public async Task<Result<DownloadDocumentResponse>> Handle(
        DownloadDocumentQuery query,
        CancellationToken cancellationToken)
    {
        Document? document = await context.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == query.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<DownloadDocumentResponse>(DocumentErrors.NotFound(query.DocumentId));
        }

        // Authorization: user can download own docs; admin/contabil can download any
        User? requestingUser = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == query.RequestingUserId, cancellationToken);

        if (requestingUser is null)
        {
            return Result.Failure<DownloadDocumentResponse>(DocumentErrors.AccessDenied);
        }

        bool isOwner = document.UserId == query.RequestingUserId;
        bool isPrivileged = requestingUser.Role is UserRole.Admin or UserRole.Contabil;

        if (!isOwner && !isPrivileged)
        {
            return Result.Failure<DownloadDocumentResponse>(DocumentErrors.AccessDenied);
        }

        Stream decryptedStream = await fileEncryptionService.DecryptAndReadAsync(
            document.EncryptedFilePath,
            document.EncryptionIv,
            cancellationToken);

        return new DownloadDocumentResponse(
            decryptedStream,
            document.ContentType,
            document.OriginalFileName);
    }
}

using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Rentals.Documents;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Queries.GetRentalDocuments;

public sealed record GetRentalDocumentsQuery(Guid RentalId) : IQuery<List<GeneratedDocumentDto>>;

internal sealed class GetRentalDocumentsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetRentalDocumentsQuery, List<GeneratedDocumentDto>>
{
    public async Task<Result<List<GeneratedDocumentDto>>> Handle(
        GetRentalDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        bool owns = await context.Rentals
            .AsNoTracking()
            .AnyAsync(r => r.Id == query.RentalId && r.OwnerUserId == userContext.UserId, cancellationToken);

        if (!owns)
        {
            return Result.Failure<List<GeneratedDocumentDto>>(
                Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        List<GeneratedDocument> documents = await context.GeneratedDocuments
            .AsNoTracking()
            .Where(d => d.RentalId == query.RentalId)
            .OrderByDescending(d => d.GeneratedAtUtc)
            .ToListAsync(cancellationToken);

        return Result.Success(documents
            .Select(d => new GeneratedDocumentDto(
                d.Id, d.Type, d.Status.ToString(), d.Version, d.DocumentId,
                d.SignedDocumentId, d.GeneratedAtUtc, d.SentAtUtc, d.SentToEmail, d.SignedAtUtc))
            .ToList());
    }
}

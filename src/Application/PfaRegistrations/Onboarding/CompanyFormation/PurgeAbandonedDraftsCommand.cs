using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>
/// Minimizare GDPR (spec §8): un dosar rămas în ciornă 90 de zile se șterge. Doar ciornele —
/// un dosar semnat sau redeschis pentru corecturi rămâne, indiferent cât stă.
/// </summary>
public sealed record PurgeAbandonedCompanyFormationDraftsCommand : ICommand<int>;

internal sealed class PurgeAbandonedCompanyFormationDraftsCommandHandler(IApplicationDbContext context)
    : ICommandHandler<PurgeAbandonedCompanyFormationDraftsCommand, int>
{
    private const int RetentionDays = 90;

    public async Task<Result<int>> Handle(
        PurgeAbandonedCompanyFormationDraftsCommand command,
        CancellationToken cancellationToken)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

        List<CompanyFormationRequest> abandoned = await context.CompanyFormationRequests
            .Where(r => r.Status == CompanyFormationStatus.Draft && r.UpdatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (abandoned.Count == 0)
        {
            return Result.Success(0);
        }

        // Proprietarii, consimțămintele și semnătura atârnă de dosar cu cascade delete.
        context.CompanyFormationRequests.RemoveRange(abandoned);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(abandoned.Count);
    }
}

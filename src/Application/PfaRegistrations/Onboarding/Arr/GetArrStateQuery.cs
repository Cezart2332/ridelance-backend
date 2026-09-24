using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Arr;

public sealed record GetArrStateQuery(Guid UserId) : IQuery<ArrStateResponse?>;

internal sealed class GetArrStateQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetArrStateQuery, ArrStateResponse?>
{
    public async Task<Result<ArrStateResponse?>> Handle(GetArrStateQuery query, CancellationToken cancellationToken)
    {
        ArrAuthorizationRequest? request = await context.ArrAuthorizationRequests
            .AsNoTracking()
            .Where(a => a.PfaRegistration.UserId == query.UserId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
        {
            return Result.Success<ArrStateResponse?>(null);
        }

        DateTime? validatedAtUtc = await context.PfaRegistrations
            .AsNoTracking()
            .Where(r => r.Id == request.PfaRegistrationId)
            .Select(r => r.ArrDossierDocumentsValidatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        DossierReadiness readiness = await DossierAttachments.ReadinessAsync(
            context,
            query.UserId,
            OnboardingSectionCatalog.RequirementsFor(OnboardingSectionKey.AutorizatieTransport),
            validatedAtUtc,
            cancellationToken);

        return Result.Success<ArrStateResponse?>(ArrShared.ToResponse(request) with
        {
            DossierPendingReview = [.. readiness.Missing, .. readiness.Unverified],
            DossierMissing = readiness.Missing,
            DossierAwaitingValidation = readiness.Unverified,
        });
    }
}

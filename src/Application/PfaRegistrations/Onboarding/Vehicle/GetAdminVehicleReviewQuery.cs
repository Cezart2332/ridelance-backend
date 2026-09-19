using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Vehicle;

/// <summary>
/// Pasul 6 văzut din admin: mașina, perioada cerută pentru copia conformă, ecusoanele și dosarul.
///
/// Adminul vedea doar documentele, deci nu știa pe ce perioadă a cerut clientul copia — exact
/// informația cu care se depune dosarul. Se folosește starea clientului, nu o copie a ei: ce vede
/// adminul e ce vede șoferul.
/// </summary>
public sealed record GetAdminVehicleReviewQuery(Guid RegistrationId) : IQuery<VehicleStateResponse>;

internal sealed class GetAdminVehicleReviewQueryHandler(
    IApplicationDbContext context,
    IQueryHandler<GetVehicleStateQuery, VehicleStateResponse> vehicleState)
    : IQueryHandler<GetAdminVehicleReviewQuery, VehicleStateResponse>
{
    public async Task<Result<VehicleStateResponse>> Handle(
        GetAdminVehicleReviewQuery query,
        CancellationToken cancellationToken)
    {
        Guid? userId = await context.PfaRegistrations
            .AsNoTracking()
            .Where(r => r.Id == query.RegistrationId)
            .Select(r => (Guid?)r.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is not Guid owner)
        {
            return Result.Failure<VehicleStateResponse>(PfaRegistrationErrors.NotFound(query.RegistrationId));
        }

        return await vehicleState.Handle(new GetVehicleStateQuery(owner), cancellationToken);
    }
}

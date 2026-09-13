using Application.Abstractions.Data;
using Domain.Cars;
using Microsoft.EntityFrameworkCore;

namespace Application.Cars;

/// <summary>Anunțurile active incluse în abonament: câte sunt, câte s-au folosit, câte mai rămân.</summary>
public sealed record ListingQuotaDto(int Included, int Used, int Remaining);

/// <summary>
/// Calculul locurilor de anunț ale unei flote, scris o singură dată — îl folosesc publicarea,
/// aprobarea și paginile care arată câte anunțuri mai sunt disponibile.
/// </summary>
public static class ListingQuota
{
    /// <summary>Câte locuri ocupă acum proprietarul. <paramref name="excludingCarId" /> nu se numără.</summary>
    public static Task<int> CountUsedAsync(
        IApplicationDbContext context,
        Guid ownerUserId,
        Guid? excludingCarId,
        CancellationToken cancellationToken) =>
        context.Cars.CountAsync(
            c => c.PostedByUserId == ownerUserId
                && c.ListingStatus == ListingStatus.Published
                && (excludingCarId == null || c.Id != excludingCarId),
            cancellationToken);

    public static async Task<ListingQuotaDto> GetAsync(
        IApplicationDbContext context,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        int used = await CountUsedAsync(context, ownerUserId, null, cancellationToken);
        return From(used);
    }

    public static ListingQuotaDto From(int used) => new(
        ListingAllowance.IncludedInFleetPlan,
        used,
        Math.Max(0, ListingAllowance.IncludedInFleetPlan - used));
}

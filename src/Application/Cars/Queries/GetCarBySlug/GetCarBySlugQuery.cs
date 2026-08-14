using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Cars;
using Application.Cars.Queries.GetAllCars;
using SharedKernel;

namespace Application.Cars.Queries.GetCarBySlug;

/// <summary>Pagina publică de detaliu, deschisă după slug-ul din URL.</summary>
public sealed record GetCarBySlugQuery(string Slug) : IQuery<CarDto>;

internal sealed class GetCarBySlugQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetCarBySlugQuery, CarDto>
{
    public Task<Result<CarDto>> Handle(GetCarBySlugQuery query, CancellationToken cancellationToken)
    {
        // Slug-urile sunt minuscule prin construcție (`CarSlug`), deci normalizarea e tot în jos —
        // ToUpperInvariant, pe care îl cere CA1308, ar garanta zero potriviri.
#pragma warning disable CA1308
        string slug = query.Slug.Trim().ToLowerInvariant();
#pragma warning restore CA1308
        return CarDetailLoader.LoadAsync(context, c => c.Slug == slug, cancellationToken);
    }
}

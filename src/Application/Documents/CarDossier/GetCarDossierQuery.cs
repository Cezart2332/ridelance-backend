using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.Expiry;
using Domain.Cars;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.CarDossier;

/// <summary>Dosarul unei mașini: documentele ei și cât de complet e.</summary>
public sealed record GetCarDossierQuery(Guid CarId) : IQuery<CarDossierDto>;

public sealed record CarDossierDto(
    /// <summary>0–100. Cât din documentele așteptate există și nu sunt expirate.</summary>
    int CompletionPercent,
    IReadOnlyList<CarDossierSlotDto> Slots);

/// <param name="State">`missing`, `valid`, `expiring_soon` sau `expired`.</param>
public sealed record CarDossierSlotDto(
    string Category,
    string Label,
    bool Required,
    Guid? DocumentId,
    string? FileName,
    DateTime? ExpiresAtUtc,
    string State);

internal sealed class GetCarDossierQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetCarDossierQuery, CarDossierDto>
{
    /// <summary>
    /// Ce se așteaptă în dosarul unei mașini de flotă.
    /// </summary>
    /// <remarks>
    /// Doar primele patru intră în procent. Copia conformă și rovinieta există la fel de real, dar
    /// nu orice mașină are nevoie de ele — numărate ca obligatorii, ar fi ținut orice dosar sub
    /// 100% fără ca proprietarul să poată face ceva.
    /// </remarks>
    private static readonly (DocumentCategory Category, string Label, bool Required)[] Slots =
    [
        (DocumentCategory.Talon, "Talon", true),
        (DocumentCategory.RCA, "RCA", true),
        (DocumentCategory.ITP, "ITP", true),
        (DocumentCategory.Casco, "CASCO", true),
        (DocumentCategory.CopieConforma, "Copie conformă", false),
        (DocumentCategory.ContractVehicul, "Contract vehicul", false),
    ];

    public async Task<Result<CarDossierDto>> Handle(
        GetCarDossierQuery query,
        CancellationToken cancellationToken)
    {
        Car? car = await context.Cars
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.CarId, cancellationToken);

        if (car is null)
        {
            return Result.Failure<CarDossierDto>(Error.NotFound("Car.NotFound", "Mașina nu a fost găsită."));
        }

        if (car.PostedByUserId != userContext.UserId)
        {
            return Result.Failure<CarDossierDto>(Error.Problem("Car.Forbidden", "Mașina nu îți aparține."));
        }

        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d => d.CarId == query.CarId)
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var slots = Slots
            .Select(slot =>
            {
                // Cel mai recent document din categorie. Un talon reîncărcat îl înlocuiește pe cel
                // vechi în dosar, fără să-l șteargă din istoric.
                Document? latest = documents.FirstOrDefault(d => d.Category == slot.Category);

                string state = latest is null
                    ? "missing"
                    : DocumentExpiryPolicy.Evaluate(latest.Category, latest.ExpiresAtUtc, today).State switch
                    {
                        DocumentExpiryState.Expired => "expired",
                        DocumentExpiryState.ExpiringSoon => "expiring_soon",
                        _ => "valid",
                    };

                return new CarDossierSlotDto(
                    slot.Category.ToString(),
                    slot.Label,
                    slot.Required,
                    latest?.Id,
                    latest?.OriginalFileName,
                    latest?.ExpiresAtUtc,
                    state);
            })
            .ToList();

        var required = slots.Where(s => s.Required).ToList();
        int satisfied = required.Count(s => s.State is "valid" or "expiring_soon");
        int percent = required.Count == 0 ? 100 : (int)Math.Round(satisfied * 100.0 / required.Count);

        return Result.Success(new CarDossierDto(percent, slots));
    }
}

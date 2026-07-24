using Application.Abstractions.Data;
using Domain.Documents;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;

namespace Application.Documents.ExtractedFields;

/// <summary>
/// Propagă o valoare confirmată de om către coloana de business (sursa de adevăr).
/// Tabelul <see cref="ExtractedField"/> rămâne stratul de proveniență; aici scriem
/// în <c>PfaRegistration</c>/<c>PfaVehicle</c>. Valoarea confirmată câștigă întotdeauna.
/// </summary>
internal static class ExtractedFieldApplier
{
    public static async Task ApplyAsync(
        IApplicationDbContext context,
        Document document,
        string fieldKey,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return;
        }

        string key = fieldKey.Trim().ToUpperInvariant();

        switch (key)
        {
            case "CUI":
            case "LEGAL_NAME":
            case "REGISTRY_NUMBER":
                await ApplyToRegistrationAsync(context, document, key, normalizedValue, cancellationToken);
                break;

            case "PLATE_NUMBER":
            case "VIN":
            case "MAKE":
            case "MODEL":
                await ApplyToVehicleAsync(context, document, key, normalizedValue, cancellationToken);
                break;

            default:
                break;
        }
    }

    private static async Task ApplyToRegistrationAsync(
        IApplicationDbContext context,
        Document document,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await FindRegistrationAsync(context, document, cancellationToken);
        if (registration is null)
        {
            return;
        }

        switch (key)
        {
            case "CUI":
                registration.Cui = value;
                break;
            case "LEGAL_NAME":
                registration.LegalName = value;
                break;
            case "REGISTRY_NUMBER":
                registration.RegistryNumber = value;
                break;
            default:
                break;
        }
    }

    private static async Task ApplyToVehicleAsync(
        IApplicationDbContext context,
        Document document,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        PfaVehicle? vehicle = await FindVehicleAsync(context, document, cancellationToken);
        if (vehicle is null)
        {
            return;
        }

        switch (key)
        {
            case "PLATE_NUMBER":
                vehicle.PlateNumber = value;
                break;
            case "VIN":
                vehicle.Vin = value;
                break;
            case "MAKE":
                vehicle.Make = value;
                break;
            case "MODEL":
                vehicle.Model = value;
                break;
            default:
                break;
        }

        vehicle.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static Task<PfaRegistration?> FindRegistrationAsync(
        IApplicationDbContext context,
        Document document,
        CancellationToken cancellationToken)
    {
        if (document.PfaRegistrationId is Guid regId)
        {
            return context.PfaRegistrations.FirstOrDefaultAsync(r => r.Id == regId, cancellationToken);
        }

        return context.PfaRegistrations
            .Where(r => r.UserId == document.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<PfaVehicle?> FindVehicleAsync(
        IApplicationDbContext context,
        Document document,
        CancellationToken cancellationToken)
    {
        if (document.PfaVehicleId is Guid vehicleId)
        {
            return await context.PfaVehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        }

        PfaRegistration? registration = await FindRegistrationAsync(context, document, cancellationToken);
        if (registration is null)
        {
            return null;
        }

        return await context.PfaVehicles
            .Where(v => v.PfaRegistrationId == registration.Id)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

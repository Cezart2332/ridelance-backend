using System.Globalization;
using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;

namespace Application.Documents.ExtractedFields;

/// <summary>
/// Propagă valorile citite prin OCR (sau corectate de admin) către coloanele de business —
/// sursa de adevăr. Userul NU confirmă nimic: încarcă documentul, OCR-ul completează, adminul
/// verifică/corectează ulterior. Tabelul <see cref="ExtractedField"/> rămâne stratul de proveniență.
/// </summary>
public interface IExtractedFieldApplier
{
    Task ApplyAsync(Document document, string fieldKey, string normalizedValue, CancellationToken cancellationToken);
}

internal sealed class ExtractedFieldApplier(
    IApplicationDbContext context,
    ISecretProtector secretProtector) : IExtractedFieldApplier
{
    public async Task ApplyAsync(
        Document document,
        string fieldKey,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return;
        }

        switch (fieldKey.Trim().ToUpperInvariant())
        {
            // Pasul 0 — eligibilitate (CI, permis, atestat)
            case "DATE_OF_BIRTH":
            case "CATEGORY_B_OBTAINED_ON":
            case "DRIVING_CATEGORIES":
            case "LICENCE_EXPIRES_ON":
            case "ATESTAT_EXPIRES_ON":
                await ApplyToEligibilityAsync(document, fieldKey, normalizedValue, cancellationToken);
                break;

            // Pasul 1, ramura „Nu am PFA" — datele de identitate citite din cartea de identitate.
            case "NUME":
            case "PRENUME":
            case "CNP":
            case "SERIE_ACT":
            case "NUMAR_ACT":
            case "AUTORITATE_EMITENTA":
            case "DATA_EMITERII":
            case "DATA_EXPIRARII":
            case "DOMICILIU_JUDET":
            case "DOMICILIU_LOCALITATE":
            case "DOMICILIU_STRADA":
            case "DOMICILIU_NUMAR":
            case "DOMICILIU_BLOC":
            case "DOMICILIU_SCARA":
            case "DOMICILIU_ETAJ":
            case "DOMICILIU_APARTAMENT":
                await ApplyToCompanyFormationAsync(document, fieldKey, normalizedValue, cancellationToken);
                break;

            // Pasul 1 — dosarul PFA
            case "CUI":
            case "LEGAL_NAME":
            case "REGISTRY_NUMBER":
            case "CAEN_CODES":
                await ApplyToRegistrationAsync(document, fieldKey, normalizedValue, cancellationToken);
                break;

            // Pasul 2 — contul bancar
            case "IBAN":
                await ApplyIbanAsync(document, normalizedValue, cancellationToken);
                break;

            // Pasul 3 — autorizația ARR
            case "AUTHORIZATION_NUMBER":
            case "AUTHORIZATION_EXPIRES_ON":
                await ApplyToArrAsync(document, fieldKey, normalizedValue, cancellationToken);
                break;

            // Pasul 5 — vehiculul și copia conformă
            case "PLATE_NUMBER":
            case "VIN":
            case "MAKE":
            case "MODEL":
                await ApplyToVehicleAsync(document, fieldKey, normalizedValue, cancellationToken);
                break;
            case "COPY_CONFORMA_NUMBER":
            case "COPY_CONFORMA_EXPIRES_ON":
                await ApplyToCopyRequestAsync(document, fieldKey, normalizedValue, cancellationToken);
                break;

            default:
                break;
        }
    }

    private async Task ApplyToEligibilityAsync(
        Document document, string key, string value, CancellationToken cancellationToken)
    {
        OnboardingEligibilityProfile? profile = await context.OnboardingEligibilityProfiles
            .FirstOrDefaultAsync(e => e.UserId == document.UserId, cancellationToken);

        if (profile is null)
        {
            profile = new OnboardingEligibilityProfile
            {
                Id = Guid.NewGuid(),
                UserId = document.UserId,
            };
            context.OnboardingEligibilityProfiles.Add(profile);
        }

        switch (key.Trim().ToUpperInvariant())
        {
            case "DATE_OF_BIRTH":
                profile.DateOfBirth = ParseDate(value) ?? profile.DateOfBirth;
                break;
            case "CATEGORY_B_OBTAINED_ON":
                profile.CategoryBObtainedOn = ParseDate(value) ?? profile.CategoryBObtainedOn;
                break;
            case "DRIVING_CATEGORIES":
                profile.DrivingCategories = value;
                break;
            case "LICENCE_EXPIRES_ON":
                profile.DrivingLicenceExpiresOn = ParseDate(value) ?? profile.DrivingLicenceExpiresOn;
                break;
            case "ATESTAT_EXPIRES_ON":
                profile.DriverCertificateExpiresOn = ParseDate(value) ?? profile.DriverCertificateExpiresOn;
                profile.HasDriverCertificate = true;
                break;
            default:
                break;
        }

        profile.UpdatedAtUtc = DateTime.UtcNow;

        // Reevaluăm eligibilitatea cu datele proaspăt citite.
        EligibilityEvaluation evaluation = EligibilityRules.Evaluate(
            profile.DateOfBirth,
            profile.CategoryBObtainedOn,
            profile.DrivingLicenceExpiresOn,
            profile.HasDriverCertificate,
            profile.DriverCertificateExpiresOn,
            DateOnly.FromDateTime(DateTime.UtcNow));

        profile.Status = evaluation.Status;
        profile.StatusReason = evaluation.Reasons.Count == 0 ? null : string.Join(" · ", evaluation.Reasons);
    }

    /// <summary>
    /// Precompletează dosarul de înființare din cartea de identitate. Se aplică doar pe ramura
    /// „Nu am PFA" și doar peste câmpurile pe care userul nu le-a atins: o valoare editată manual
    /// nu se suprascrie la o reîncărcare de document.
    /// </summary>
    private async Task ApplyToCompanyFormationAsync(
        Document document, string key, string value, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await FindRegistrationAsync(document, cancellationToken);
        if (registration is null || registration.RegistrationType != RegistrationType.NuAmPfa)
        {
            return;
        }

        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == registration.Id, cancellationToken);

        if (request is null)
        {
            request = new CompanyFormationRequest
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
            };
            context.CompanyFormationRequests.Add(request);
        }

        // Dosarul semnat nu se mai rescrie de OCR — semnătura e legată de datele de atunci.
        if (request.IsLocked)
        {
            return;
        }

        var prefilled = PrefilledFieldMap.Parse(request.PrefilledFields);
        string fieldName = key.Trim().ToUpperInvariant();

        if (prefilled.IsManuallyEdited(fieldName))
        {
            return;
        }

        PersoanaFizica p = request.Solicitant;

        switch (fieldName)
        {
            case "NUME":
                p.Nume = value;
                break;
            case "PRENUME":
                p.Prenume = value;
                break;
            case "CNP":
                // Ca la IBAN: în clar nu rămâne decât masca.
                p.CnpEncrypted = secretProtector.Protect(value);
                p.CnpMasked = CnpValidator.Mask(value);
                break;
            case "SERIE_ACT":
                p.SerieAct = value.ToUpperInvariant();
                break;
            case "NUMAR_ACT":
                p.NumarAct = value;
                break;
            case "AUTORITATE_EMITENTA":
                p.AutoritateEmitenta = value;
                break;
            case "DATA_EMITERII":
                p.DataEmiterii = ParseDate(value) ?? p.DataEmiterii;
                break;
            case "DATA_EXPIRARII":
                p.DataExpirarii = ParseDate(value) ?? p.DataExpirarii;
                break;
            case "DOMICILIU_JUDET":
                p.Domiciliu.Judet = value;
                break;
            case "DOMICILIU_LOCALITATE":
                p.Domiciliu.Localitate = value;
                break;
            case "DOMICILIU_STRADA":
                p.Domiciliu.Strada = value;
                break;
            case "DOMICILIU_NUMAR":
                p.Domiciliu.Numar = value;
                break;
            case "DOMICILIU_BLOC":
                p.Domiciliu.Bloc = value;
                break;
            case "DOMICILIU_SCARA":
                p.Domiciliu.Scara = value;
                break;
            case "DOMICILIU_ETAJ":
                p.Domiciliu.Etaj = value;
                break;
            case "DOMICILIU_APARTAMENT":
                p.Domiciliu.Apartament = value;
                break;
            default:
                return;
        }

        prefilled.MarkPrefilled(fieldName);
        request.PrefilledFields = prefilled.Serialize();
        request.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task ApplyToRegistrationAsync(
        Document document, string key, string value, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await FindRegistrationAsync(document, cancellationToken);
        if (registration is null)
        {
            return;
        }

        switch (key.Trim().ToUpperInvariant())
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
            case "CAEN_CODES":
                // Coloana e un array JSON; valoarea normalizată vine ca „4939,5610".
                registration.CaenCodes = JsonSerializer.Serialize(
                    value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                break;
            default:
                break;
        }
    }

    private async Task ApplyIbanAsync(Document document, string iban, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await FindRegistrationAsync(document, cancellationToken);
        if (registration is null)
        {
            return;
        }

        PfaBankAccountDeclaration? declaration = await context.PfaBankAccountDeclarations
            .FirstOrDefaultAsync(b => b.PfaRegistrationId == registration.Id, cancellationToken);

        if (declaration is null)
        {
            declaration = new PfaBankAccountDeclaration
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
            };
            context.PfaBankAccountDeclarations.Add(declaration);
        }

        string masked = iban.Length >= 8 ? $"{iban[..4]}••••{iban[^4..]}" : "••••";

        // OCR-ul citit din document coincide cu ce era deja declarat? (verificare pentru admin)
        declaration.OcrIbanMatches = declaration.IbanMasked is null || declaration.IbanMasked == masked;

        // IBAN-ul complet se stochează DOAR criptat; în clar rămâne doar masca.
        declaration.IbanEncrypted = secretProtector.Protect(iban);
        declaration.IbanMasked = masked;
        declaration.ConfirmationDocumentId = document.Id;
        declaration.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task ApplyToArrAsync(
        Document document, string key, string value, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await FindRegistrationAsync(document, cancellationToken);
        if (registration is null)
        {
            return;
        }

        ArrAuthorizationRequest? arr = await context.ArrAuthorizationRequests
            .FirstOrDefaultAsync(a => a.PfaRegistrationId == registration.Id, cancellationToken);

        if (arr is null)
        {
            return;
        }

        switch (key.Trim().ToUpperInvariant())
        {
            case "AUTHORIZATION_NUMBER":
                arr.AuthorizationNumber = value;
                arr.AuthorizationDocumentId = document.Id;
                break;
            case "AUTHORIZATION_EXPIRES_ON":
                arr.AuthorizationExpiresOn = ParseDate(value) ?? arr.AuthorizationExpiresOn;
                break;
            default:
                break;
        }

        arr.UpdatedAtUtc = DateTime.UtcNow;
    }

    private async Task ApplyToVehicleAsync(
        Document document, string key, string value, CancellationToken cancellationToken)
    {
        PfaVehicle? vehicle = await FindVehicleAsync(document, cancellationToken);
        if (vehicle is null)
        {
            return;
        }

        switch (key.Trim().ToUpperInvariant())
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

    private async Task ApplyToCopyRequestAsync(
        Document document, string key, string value, CancellationToken cancellationToken)
    {
        PfaVehicle? vehicle = await FindVehicleAsync(document, cancellationToken);
        if (vehicle is null)
        {
            return;
        }

        VehicleCopyRequest? copy = await context.VehicleCopyRequests
            .FirstOrDefaultAsync(c => c.PfaVehicleId == vehicle.Id, cancellationToken);

        if (copy is null)
        {
            return;
        }

        switch (key.Trim().ToUpperInvariant())
        {
            case "COPY_CONFORMA_NUMBER":
                copy.CopyConformaNumber = value;
                copy.CopyConformaDocumentId = document.Id;
                break;
            case "COPY_CONFORMA_EXPIRES_ON":
                copy.ExpiresOn = ParseDate(value) ?? copy.ExpiresOn;
                break;
            default:
                break;
        }

        copy.UpdatedAtUtc = DateTime.UtcNow;
    }

    private Task<PfaRegistration?> FindRegistrationAsync(Document document, CancellationToken cancellationToken)
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

    private async Task<PfaVehicle?> FindVehicleAsync(Document document, CancellationToken cancellationToken)
    {
        if (document.PfaVehicleId is Guid vehicleId)
        {
            return await context.PfaVehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        }

        PfaRegistration? registration = await FindRegistrationAsync(document, cancellationToken);
        if (registration is null)
        {
            return null;
        }

        return await context.PfaVehicles
            .Where(v => v.PfaRegistrationId == registration.Id)
            .OrderByDescending(v => v.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static DateOnly? ParseDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d)
            ? d
            : null;
}

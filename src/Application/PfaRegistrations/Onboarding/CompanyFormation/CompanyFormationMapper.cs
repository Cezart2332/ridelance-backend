using Application.Abstractions.Security;
using Application.Documents.ExtractedFields;
using Domain.PfaRegistrations.CompanyFormation;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>
/// Proiecția dosarului de înființare spre client. <paramref name="revealCnp"/> decide dacă
/// CNP-ul iese în clar: proprietarul dosarului îl vede (altfel n-ar putea verifica ce a citit
/// OCR-ul), operatorul nu.
/// </summary>
internal static class CompanyFormationMapper
{
    public static AdresaDto ToDto(Adresa a) =>
        new(a.Judet, a.Localitate, a.Strada, a.Numar, a.Bloc, a.Scara, a.Etaj, a.Apartament, a.CodPostal);

    public static void Apply(Adresa target, AdresaPayload? payload)
    {
        if (payload is null)
        {
            return;
        }

        target.Judet = Trim(payload.Judet);
        target.Localitate = Trim(payload.Localitate);
        target.Strada = Trim(payload.Strada);
        target.Numar = Trim(payload.Numar);
        target.Bloc = Trim(payload.Bloc);
        target.Scara = Trim(payload.Scara);
        target.Etaj = Trim(payload.Etaj);
        target.Apartament = Trim(payload.Apartament);
        target.CodPostal = Trim(payload.CodPostal);
    }

    public static PersoanaFizicaDto ToDto(PersoanaFizica p, ISecretProtector protector, bool revealCnp)
    {
        string? cnp = revealCnp && !string.IsNullOrWhiteSpace(p.CnpEncrypted)
            ? protector.Unprotect(p.CnpEncrypted)
            : null;

        return new PersoanaFizicaDto(
            p.Nume,
            p.Prenume,
            cnp,
            p.CnpMasked,
            p.TipAct.ToString(),
            p.SerieAct,
            p.NumarAct,
            p.AutoritateEmitenta,
            p.DataEmiterii,
            p.DataExpirarii,
            ToDto(p.Domiciliu));
    }

    /// <summary>
    /// Scrie payload-ul peste entitate și marchează în <paramref name="prefilled"/> câmpurile pe
    /// care userul le-a schimbat față de ce citise OCR-ul. Marcajul e ce oprește o reîncărcare
    /// de CI să suprascrie o corectură.
    /// </summary>
    public static void Apply(
        PersoanaFizica target,
        PersoanaFizicaPayload payload,
        ISecretProtector protector,
        PrefilledFieldMap prefilled)
    {
        Set("NUME", target.Nume, Trim(payload.Nume), v => target.Nume = v);
        Set("PRENUME", target.Prenume, Trim(payload.Prenume), v => target.Prenume = v);
        Set("SERIE_ACT", target.SerieAct, Trim(payload.SerieAct)?.ToUpperInvariant(), v => target.SerieAct = v);
        Set("NUMAR_ACT", target.NumarAct, Trim(payload.NumarAct), v => target.NumarAct = v);
        Set("AUTORITATE_EMITENTA", target.AutoritateEmitenta, Trim(payload.AutoritateEmitenta), v => target.AutoritateEmitenta = v);

        if (Enum.TryParse(payload.TipAct, ignoreCase: true, out TipActIdentitate tipAct) && Enum.IsDefined(tipAct))
        {
            target.TipAct = tipAct;
        }

        if (target.DataEmiterii != payload.DataEmiterii)
        {
            prefilled.MarkManuallyEdited("DATA_EMITERII");
            target.DataEmiterii = payload.DataEmiterii;
        }

        if (target.DataExpirarii != payload.DataExpirarii)
        {
            prefilled.MarkManuallyEdited("DATA_EXPIRARII");
            target.DataExpirarii = payload.DataExpirarii;
        }

        string? newCnp = payload.Cnp is null ? null : new string(payload.Cnp.Where(char.IsAsciiDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(newCnp))
        {
            string? currentCnp = string.IsNullOrWhiteSpace(target.CnpEncrypted)
                ? null
                : protector.Unprotect(target.CnpEncrypted);

            if (currentCnp != newCnp)
            {
                prefilled.MarkManuallyEdited("CNP");
                target.CnpEncrypted = protector.Protect(newCnp);
                target.CnpMasked = CnpValidator.Mask(newCnp);
            }
        }

        ApplyAddress(target.Domiciliu, payload.Domiciliu, "DOMICILIU", prefilled);

        void Set(string field, string? current, string? next, Action<string?> assign)
        {
            if (current != next)
            {
                prefilled.MarkManuallyEdited(field);
                assign(next);
            }
        }
    }

    public static void ApplyAddress(Adresa target, AdresaPayload? payload, string prefix, PrefilledFieldMap prefilled)
    {
        if (payload is null)
        {
            return;
        }

        Set("JUDET", target.Judet, Trim(payload.Judet), v => target.Judet = v);
        Set("LOCALITATE", target.Localitate, Trim(payload.Localitate), v => target.Localitate = v);
        Set("STRADA", target.Strada, Trim(payload.Strada), v => target.Strada = v);
        Set("NUMAR", target.Numar, Trim(payload.Numar), v => target.Numar = v);
        Set("BLOC", target.Bloc, Trim(payload.Bloc), v => target.Bloc = v);
        Set("SCARA", target.Scara, Trim(payload.Scara), v => target.Scara = v);
        Set("ETAJ", target.Etaj, Trim(payload.Etaj), v => target.Etaj = v);
        Set("APARTAMENT", target.Apartament, Trim(payload.Apartament), v => target.Apartament = v);
        Set("COD_POSTAL", target.CodPostal, Trim(payload.CodPostal), v => target.CodPostal = v);

        void Set(string field, string? current, string? next, Action<string?> assign)
        {
            if (current != next)
            {
                prefilled.MarkManuallyEdited($"{prefix}_{field}");
                assign(next);
            }
        }
    }

    public static CompanyFormationResponse ToResponse(
        CompanyFormationRequest request,
        ISecretProtector protector,
        bool revealCnp)
    {
        var prefilled = PrefilledFieldMap.Parse(request.PrefilledFields);

        return new CompanyFormationResponse(
            request.Id,
            request.PfaRegistrationId,
            request.Status.ToString(),
            request.CurrentStage.ToString(),
            request.IsLocked,
            request.AdminNote,
            ToDto(request.Solicitant, protector, revealCnp),
            // Cheile rămân majuscule, ca pe server; frontendul compară case-insensitive.
            prefilled.PrefilledUntouched().ToList(),
            new CompanyFormationOfficeDto(
                request.OfficeType?.ToString(),
                request.ConsultoOfficeId,
                request.IsOwner,
                ToDto(request.OfficeAddress),
                request.AcknowledgedOwnershipDocs,
                request.AcknowledgedSubmitLater,
                request.AcknowledgedOwnerConsent),
            request.Owners
                .OrderBy(o => o.Position)
                .Select(o => new CompanyFormationOwnerDto(o.Id, o.Position, ToDto(o.Persoana, protector, revealCnp)))
                .ToList(),
            request.PersonalDataComplete,
            request.RegisteredOfficeComplete,
            request.Signature is null
                ? null
                : new CompanyFormationSignatureDto(request.Signature.SignedAtUtc, request.Signature.ImageDocumentId));
    }

    /// <summary>Dosarul gol, pentru userii care n-au ajuns încă la acest pas.</summary>
    public static CompanyFormationResponse Empty() =>
        new(
            null,
            null,
            CompanyFormationStatus.Draft.ToString(),
            CompanyFormationStage.PersonalData.ToString(),
            false,
            null,
            new PersoanaFizicaDto(
                null, null, null, null,
                TipActIdentitate.CI.ToString(),
                null, null, null, null, null,
                ToDto(new Adresa())),
            [],
            new CompanyFormationOfficeDto(null, null, null, ToDto(new Adresa()), false, false, null),
            [],
            false,
            false,
            null);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

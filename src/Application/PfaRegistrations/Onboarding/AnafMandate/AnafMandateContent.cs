using System.Globalization;
using Application.Abstractions.Dossiers;
using Domain.PfaRegistrations.CompanyFormation;

namespace Application.PfaRegistrations.Onboarding.AnafMandate;

/// <summary>
/// Ce intră în împuternicirea ANAF și ce lipsește din ea. Pur, fără bază de date: sursele
/// (dosarul de înființare, câmpurile citite din buletin) le adună <see cref="AnafMandateService"/>
/// în aceleași chei ca OCR-ul (<c>nume</c>, <c>cnp</c>, <c>domiciliu_strada</c>…).
/// </summary>
public static class AnafMandateContent
{
    /// <summary>Datele solicitantului din dosarul de înființare, în cheile OCR.</summary>
    public static Dictionary<string, string> FieldsOf(PersoanaFizica person, string? cnp)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Put(fields, "nume", person.Nume);
        Put(fields, "prenume", person.Prenume);
        Put(fields, "cnp", cnp);
        Put(fields, "serie_act", person.SerieAct);
        Put(fields, "numar_act", person.NumarAct);
        Put(fields, "autoritate_emitenta", person.AutoritateEmitenta);
        Put(fields, "data_emiterii", person.DataEmiterii?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Put(fields, "domiciliu_judet", person.Domiciliu.Judet);
        Put(fields, "domiciliu_localitate", person.Domiciliu.Localitate);
        Put(fields, "domiciliu_strada", person.Domiciliu.Strada);
        Put(fields, "domiciliu_numar", person.Domiciliu.Numar);
        Put(fields, "domiciliu_bloc", person.Domiciliu.Bloc);
        Put(fields, "domiciliu_scara", person.Domiciliu.Scara);
        Put(fields, "domiciliu_etaj", person.Domiciliu.Etaj);
        Put(fields, "domiciliu_apartament", person.Domiciliu.Apartament);

        return fields;
    }

    /// <summary>Completează golurile din <paramref name="primary"/> cu valorile din <paramref name="fallback"/>.</summary>
    public static void FillGaps(Dictionary<string, string> primary, IReadOnlyDictionary<string, string> fallback)
    {
        foreach ((string key, string value) in fallback)
        {
            if (!primary.ContainsKey(key))
            {
                Put(primary, key, value);
            }
        }
    }

    /// <summary>
    /// Adresa pe un rând: „Cluj-Napoca, jud. Cluj, str. Memorandumului nr. 28, bl. A,
    /// sc. 1, et. 2, ap. 7”. Null dacă lipsesc strada sau numărul — fără ele adresa nu identifică pe nimeni.
    /// </summary>
    public static string? Address(
        string? county, string? locality, string? street, string? number,
        string? block = null, string? entrance = null, string? floor = null, string? apartment = null)
    {
        if (string.IsNullOrWhiteSpace(locality) || string.IsNullOrWhiteSpace(street) || string.IsNullOrWhiteSpace(number))
        {
            return null;
        }

        var parts = new List<string> { locality.Trim() };

        // București n-are județ: „București, jud. București” ar fi o repetiție.
        if (!string.IsNullOrWhiteSpace(county) && !SamePlace(county, locality))
        {
            parts.Add($"jud. {county.Trim()}");
        }

        parts.Add($"str. {street.Trim()} nr. {number.Trim()}");
        AddIf(parts, "bl.", block);
        AddIf(parts, "sc.", entrance);
        AddIf(parts, "et.", floor);
        AddIf(parts, "ap.", apartment);

        return string.Join(", ", parts);
    }

    public static string? Address(Adresa address) =>
        Address(address.Judet, address.Localitate, address.Strada, address.Numar,
            address.Bloc, address.Scara, address.Etaj, address.Apartament);

    /// <summary>Titularul, din câmpurile de identitate și datele PFA-ului.</summary>
    public static AnafMandant Mandant(
        IReadOnlyDictionary<string, string> identity,
        string? pfaName,
        string? professionalOffice,
        string? cui,
        string? registryNumber)
    {
        string? fullName = FullName(identity);

        return new AnafMandant(
            fullName,
            Get(identity, "cnp"),
            Address(
                Get(identity, "domiciliu_judet"),
                Get(identity, "domiciliu_localitate"),
                Get(identity, "domiciliu_strada"),
                Get(identity, "domiciliu_numar"),
                Get(identity, "domiciliu_bloc"),
                Get(identity, "domiciliu_scara"),
                Get(identity, "domiciliu_etaj"),
                Get(identity, "domiciliu_apartament")),
            Get(identity, "serie_act")?.ToUpperInvariant(),
            Get(identity, "numar_act"),
            Get(identity, "autoritate_emitenta"),
            DateOnly.TryParseExact(Get(identity, "data_emiterii"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly issued) ? issued : null,
            Clean(pfaName),
            Clean(professionalOffice),
            Clean(cui),
            Clean(registryNumber));
    }

    /// <summary>Ce nu s-a găsit, pe înțelesul adminului. Gol = împuternicirea e completă.</summary>
    public static IReadOnlyList<string> Missing(AnafMandant mandant, AnafMandatar mandatar)
    {
        var missing = new List<string>();

        AddMissing(missing, mandant.FullName, "numele titularului");
        AddMissing(missing, mandant.Cnp, "CNP-ul titularului");
        AddMissing(missing, mandant.Domicile, "domiciliul (localitate, stradă, număr)");
        AddMissing(missing, mandant.IdSeries, "seria CI");
        AddMissing(missing, mandant.IdNumber, "numărul CI");
        AddMissing(missing, mandant.IdIssuer, "emitentul CI");
        if (mandant.IdIssuedOn is null)
        {
            missing.Add("data eliberării CI");
        }
        AddMissing(missing, mandant.PfaName, "denumirea PFA");
        AddMissing(missing, mandant.ProfessionalOffice, "sediul profesional");
        AddMissing(missing, mandant.Cui, "CUI-ul");

        bool mandatarIncomplete =
            string.IsNullOrWhiteSpace(mandatar.FullName) ||
            string.IsNullOrWhiteSpace(mandatar.Cnp) ||
            string.IsNullOrWhiteSpace(mandatar.Domicile) ||
            string.IsNullOrWhiteSpace(mandatar.IdSeries) ||
            string.IsNullOrWhiteSpace(mandatar.IdNumber) ||
            string.IsNullOrWhiteSpace(mandatar.Email);

        if (mandatarIncomplete)
        {
            missing.Add("datele mandatarului (configurarea serverului, AnafMandatar)");
        }

        return missing;
    }

    /// <summary>„Lipsește: CNP-ul titularului, seria CI.” — nota de pe document, văzută de admin.</summary>
    public static string? Note(IReadOnlyList<string> missing) =>
        missing.Count == 0 ? null : $"Lipsește: {string.Join(", ", missing)}.";

    /// <summary>Numele de familie primul, cu majuscule, ca la mandatar: „POPESCU ION-ANDREI”.</summary>
    private static string? FullName(IReadOnlyDictionary<string, string> identity)
    {
        string? last = Get(identity, "nume");
        string? first = Get(identity, "prenume");

        string? name = last is not null && first is not null
            ? $"{last} {first}"
            : Get(identity, "full_name");

        return name is null
            ? null
            : string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpper(CultureInfo.GetCultureInfo("ro-RO"));
    }

    private static bool SamePlace(string county, string locality)
    {
        string a = county.Trim();
        string b = locality.Trim();
        return a.Contains("Bucure", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIf(List<string> parts, string prefix, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{prefix} {value.Trim()}");
        }
    }

    private static void AddMissing(List<string> missing, string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(label);
        }
    }

    private static string? Get(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out string? value) ? Clean(value) : null;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Put(Dictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields[key] = value.Trim();
        }
    }
}

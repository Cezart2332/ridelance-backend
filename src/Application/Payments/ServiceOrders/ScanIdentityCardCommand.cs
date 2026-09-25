using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Application.Abstractions.Ai;
using Application.Abstractions.Messaging;
using Application.Documents.AiVerification;
using Application.PfaRegistrations.Onboarding.CompanyFormation;
using Domain.Documents;
using Domain.PfaRegistrations.CompanyFormation;
using SharedKernel;

namespace Application.Payments.ServiceOrders;

/// <summary>
/// Citește buletinul încărcat în formularul unui serviciu, ca datele personale să se completeze
/// singure — ca în onboarding, dar fără cont.
///
/// Nu salvează nimic: e o citire, nu o depunere. Actul ajunge în dosar abia odată cu comanda.
/// Fiind public, fiecare citire costă un apel la model plătit de noi; de aceea e plafonată pe
/// adresa IP și, peste tot, pe zi.
/// </summary>
/// <param name="ClientKey">Cine cere — adresa IP. Folosită doar pentru plafon, nu se păstrează.</param>
public sealed record ScanIdentityCardCommand(
    string FileName,
    Stream FileStream,
    string ContentType,
    long FileSize,
    string ClientKey) : ICommand<IdentityCardScan>;

/// <summary>
/// Ce s-a putut citi, în forma formularului. Câmpurile necitite (sau invalide, ca un CNP cu
/// cifra de control greșită) rămân nule, iar <paramref name="PrefilledFields"/> spune care au
/// venit din act — formularul le marchează „completat automat".
/// </summary>
public sealed record IdentityCardScan(
    string? Nume,
    string? Prenume,
    string? Cnp,
    string? SerieAct,
    string? NumarAct,
    string? AutoritateEmitenta,
    DateOnly? DataEmiterii,
    DateOnly? DataExpirarii,
    AdresaDto Domiciliu,
    IReadOnlyList<string> PrefilledFields,
    string? Note);

internal sealed class ScanIdentityCardCommandHandler(
    IDocumentAiAnalyzer analyzer,
    IAiUsageLimiter limiter)
    : ICommandHandler<ScanIdentityCardCommand, IdentityCardScan>
{
    private static readonly string[] AllowedTypes = ["APPLICATION/PDF", "IMAGE/JPEG", "IMAGE/JPG", "IMAGE/PNG", "IMAGE/WEBP"];
    private const long MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>Cât poate citi o adresă IP: câteva reîncercări pe o poză proastă, nu mai mult.</summary>
    private const int CallsPerClient = 8;
    private static readonly TimeSpan ClientWindow = TimeSpan.FromHours(1);

    /// <summary>Plafonul întregului site, ca un abuz să nu poată crește factura fără limită.</summary>
    private const int CallsPerDay = 400;

    private const string Feature = "public-id-scan";

    public async Task<Result<IdentityCardScan>> Handle(
        ScanIdentityCardCommand command,
        CancellationToken cancellationToken)
    {
        if (!AllowedTypes.Contains(command.ContentType.ToUpperInvariant()))
        {
            return Result.Failure<IdentityCardScan>(Error.Problem(
                "IdentityScan.InvalidType", "Acceptăm o poză (JPG, PNG, WebP) sau un PDF."));
        }

        if (command.FileSize > MaxSizeBytes)
        {
            return Result.Failure<IdentityCardScan>(Error.Problem(
                "IdentityScan.TooLarge", "Fișierul e prea mare. Maximum 10 MB."));
        }

        if (!limiter.TryConsume(ClientId(command.ClientKey), Feature, CallsPerClient, ClientWindow)
            || !limiter.TryConsume(Guid.Empty, $"{Feature}-all", CallsPerDay, TimeSpan.FromDays(1)))
        {
            return Result.Failure<IdentityCardScan>(Error.Problem(
                "IdentityScan.Limit", "Ai încercat de prea multe ori. Completează datele de mână sau revino peste o oră."));
        }

        // Aceeași descriere și aceleași câmpuri ca la buletinul din onboarding: un singur prompt.
        DocumentAiExpectation expectation = DocumentAiCatalog.For(DocumentCategory.CarteIdentitate)!;
        IReadOnlyList<ExtractedFieldSpec> specs = expectation.FieldSpecs;

        using var memory = new MemoryStream();
        await command.FileStream.CopyToAsync(memory, cancellationToken);

        Result<DocumentAiAnalysisResult> analysis = await analyzer.AnalyzeAsync(
            new DocumentAiAnalysisRequest(
                memory.ToArray(),
                command.ContentType,
                command.FileName,
                expectation.Label,
                expectation.Details,
                ExpectsExpiryDate: true,
                specs.Select(f => new AiFieldRequest(f.Key, f.Description, f.Type.ToString(), f.Required)).ToList()),
            cancellationToken);

        if (analysis.IsFailure)
        {
            return Result.Failure<IdentityCardScan>(Error.Problem(
                "IdentityScan.Failed",
                analysis.Error.Code == "Ai.NotConfigured"
                    ? "Citirea automată nu e disponibilă acum. Completează datele de mână."
                    : "Nu am putut citi actul. Încearcă o poză mai clară sau completează datele de mână."));
        }

        return Result.Success(IdentityCardReader.Read(analysis.Value, specs));
    }

    /// <summary>IP-ul, ca cheie de plafon. Hash, nu IP-ul în clar: cheia stă în memoria procesului.</summary>
    private static Guid ClientId(string clientKey) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(clientKey))[..16]);
}

/// <summary>
/// Traduce răspunsul modelului în câmpurile formularului. Separat de handler, ca regulile
/// (ce se păstrează, ce se aruncă) să poată fi testate fără model.
/// </summary>
internal static class IdentityCardReader
{
    public static IdentityCardScan Read(DocumentAiAnalysisResult result, IReadOnlyList<ExtractedFieldSpec> specs)
    {
        var prefilled = new List<string>();

        string? Get(string key)
        {
            ExtractedFieldSpec? spec = specs.FirstOrDefault(s => s.Key == key);
            AiFieldResult? field = result.Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            if (spec is null || field is null)
            {
                return null;
            }

            string? normalized = ExtractedFieldValidators.Normalize(spec.Type, field.Value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            // Un CNP sau o dată care nu trece validatorul nu se precompletează: un câmp gol se
            // vede, unul greșit dar completat trece neobservat până la ONRC.
            if (spec.Type is ExtractedFieldType.Cnp or ExtractedFieldType.Date
                && !ExtractedFieldValidators.Validate(spec.Type, normalized))
            {
                return null;
            }

            prefilled.Add(key.ToUpperInvariant());
            return normalized;
        }

        string? nume = Get("nume");
        string? prenume = Get("prenume");

        // Numele întreg e plasa de siguranță când modelul nu le-a separat: pe buletin numele de
        // familie e primul.
        if ((nume is null || prenume is null) && Get("full_name") is string full)
        {
            string[] parts = full.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                nume ??= parts[0];
                prenume ??= parts[1];
                prefilled.Add("NUME");
                prefilled.Add("PRENUME");
            }
        }

        prefilled.Remove("FULL_NAME");

        var domiciliu = new AdresaDto(
            Get("domiciliu_judet"),
            Get("domiciliu_localitate"),
            Get("domiciliu_strada"),
            Get("domiciliu_numar"),
            Get("domiciliu_bloc"),
            Get("domiciliu_scara"),
            Get("domiciliu_etaj"),
            Get("domiciliu_apartament"),
            null);

        var scan = new IdentityCardScan(
            nume,
            prenume,
            Get("cnp"),
            Get("serie_act")?.ToUpperInvariant(),
            Get("numar_act"),
            Get("autoritate_emitenta"),
            ParseDate(Get("data_emiterii")),
            ParseDate(Get("data_expirarii")),
            domiciliu,
            prefilled.Distinct(StringComparer.Ordinal).ToList(),
            null);

        return scan with { Note = NoteFor(result, scan) };
    }

    private static string? NoteFor(DocumentAiAnalysisResult result, IdentityCardScan scan)
    {
        if (scan.PrefilledFields.Count > 0)
        {
            return scan.Cnp is null ? "N-am putut citi CNP-ul. Verifică-l și completează-l de mână." : null;
        }

        if (!result.MatchesExpectedType)
        {
            return "Documentul nu pare a fi un act de identitate. Încarcă buletinul sau completează datele de mână.";
        }

        return "N-am putut citi datele din poză. Încearcă o poză mai clară sau completează-le de mână.";
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)
            ? parsed
            : null;
}

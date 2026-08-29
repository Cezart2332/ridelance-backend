using Application.Abstractions.Ai;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.AiVerification;
using Domain.Documents;
using Domain.Users;
using SharedKernel;

namespace Application.Cars.Commands.ScanRegistration;

/// <summary>
/// Citește talonul o singură dată, ca să precompleteze numărul de înmatriculare și VIN-ul.
///
/// Nu salvează nimic: fișierul nu devine document în dosar, nu se criptează și nu se ține minte.
/// E o citire, nu o depunere — cine vrea talonul în dosarul mașinii îl încarcă separat, de acolo.
/// Motivul practic: numărul se cere în formularul de adăugare, cu mult înainte ca dosarul să
/// existe, iar pozele anunțului nu ajută — lor li se estompează plăcuța la încărcare, deliberat.
/// </summary>
public sealed record ScanVehicleRegistrationCommand(
    string FileName,
    Stream FileStream,
    string ContentType,
    long FileSize) : ICommand<VehicleRegistrationScan>;

/// <summary>
/// O valoare citită din talon.
/// </summary>
/// <param name="MatchesFormat">
/// Dacă trece validatorul determinist al tipului (format de plăcuță RO, VIN de 17 caractere).
/// Valoarea se întoarce și când nu trece: e mai util să vadă omul ce s-a citit și să corecteze,
/// decât să primească un câmp gol fără explicație. Interfața o marchează ca nesigură.
/// </param>
public sealed record ScannedValue(string Value, bool MatchesFormat, double Confidence);

/// <param name="Note">
/// De ce n-a ieșit nimic, când n-a ieșit nimic. Gol când citirea a mers.
/// </param>
public sealed record VehicleRegistrationScan(
    ScannedValue? PlateNumber,
    ScannedValue? Vin,
    string? Note);

internal sealed class ScanVehicleRegistrationCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IDocumentAiAnalyzer analyzer)
    : ICommandHandler<ScanVehicleRegistrationCommand, VehicleRegistrationScan>
{
    private static readonly string[] AllowedTypes = ["APPLICATION/PDF", "IMAGE/JPEG", "IMAGE/JPG", "IMAGE/PNG"];
    private const long MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>Cheile din <see cref="DocumentAiCatalog"/> care ne interesează aici.</summary>
    private const string PlateKey = "plate_number";
    private const string VinKey = "vin";

    public async Task<Result<VehicleRegistrationScan>> Handle(
        ScanVehicleRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        if (!AllowedTypes.Contains(command.ContentType.ToUpperInvariant()))
        {
            return Result.Failure<VehicleRegistrationScan>(Error.Problem(
                "VehicleScan.InvalidType",
                "Acceptăm doar PDF, JPEG sau PNG."));
        }

        if (command.FileSize > MaxSizeBytes)
        {
            return Result.Failure<VehicleRegistrationScan>(Error.Problem(
                "VehicleScan.TooLarge",
                "Fișierul e prea mare. Maximum 10 MB."));
        }

        Result<User> user = await CarAccessHelper.GetCurrentUserAsync(context, userContext, cancellationToken);
        if (user.IsFailure)
        {
            return Result.Failure<VehicleRegistrationScan>(user.Error);
        }

        // Aceeași poartă ca la adăugarea unei mașini: scanarea costă un apel la model, deci nu e
        // deschisă oricui are cont.
        Result access = CarAccessHelper.ValidateCarManagement(user.Value, car: null);
        if (access.IsFailure)
        {
            return Result.Failure<VehicleRegistrationScan>(access.Error);
        }

        // Descrierea documentului și a câmpurilor vin din catalog, nu scrise a doua oară aici:
        // altfel promptul de la scanare ar fi divergat în tăcere de cel de la încărcarea în dosar.
        DocumentAiExpectation? expectation = DocumentAiCatalog.For(DocumentCategory.Talon);
        if (expectation is null)
        {
            return Result.Failure<VehicleRegistrationScan>(Error.Failure(
                "VehicleScan.NotConfigured",
                "Talonul nu are o descriere de citire configurată."));
        }

        var wanted = expectation.FieldSpecs
            .Where(f => f.Key is PlateKey or VinKey)
            .ToList();

        using var memory = new MemoryStream();
        await command.FileStream.CopyToAsync(memory, cancellationToken);

        Result<DocumentAiAnalysisResult> analysis = await analyzer.AnalyzeAsync(
            new DocumentAiAnalysisRequest(
                memory.ToArray(),
                command.ContentType,
                command.FileName,
                expectation.Label,
                expectation.Details,
                ExpectsExpiryDate: false,
                wanted.Select(f => new AiFieldRequest(f.Key, f.Description, f.Type.ToString(), f.Required)).ToList()),
            cancellationToken);

        if (analysis.IsFailure)
        {
            // Eroarea modelului nu se dă mai departe ca atare: „Cheia API OpenRouter nu este
            // configurată" n-are ce căuta în fața unui proprietar de flotă. Se păstrează însă
            // distincția care contează pentru el — dacă are rost să mai încerce o dată.
            return Result.Failure<VehicleRegistrationScan>(
                analysis.Error.Code == "Ai.NotConfigured"
                    ? Error.Problem(
                        "VehicleScan.Unavailable",
                        "Scanarea talonului nu e disponibilă acum. Completează numărul manual.")
                    : Error.Problem(
                        "VehicleScan.Failed",
                        "Nu am putut citi documentul. Mai încearcă o dată sau completează numărul manual."));
        }

        return Result.Success(VehicleRegistrationReader.Read(analysis.Value, wanted));
    }
}

/// <summary>
/// Traduce răspunsul modelului în valorile de precompletat.
///
/// Stă separat de handler ca să poată fi verificat fără bază de date și fără apel la model: aici
/// se decide ce se umple în formular, deci exact partea care merită teste.
/// </summary>
internal static class VehicleRegistrationReader
{
    private const string PlateKey = "plate_number";
    private const string VinKey = "vin";

    public static VehicleRegistrationScan Read(
        DocumentAiAnalysisResult result,
        IReadOnlyList<ExtractedFieldSpec> specs)
    {
        ScannedValue? plate = ReadField(result, specs, PlateKey);
        ScannedValue? vin = ReadField(result, specs, VinKey);

        return new VehicleRegistrationScan(plate, vin, NoteFor(result, plate, vin));
    }

    private static ScannedValue? ReadField(
        DocumentAiAnalysisResult result,
        IReadOnlyList<ExtractedFieldSpec> specs,
        string key)
    {
        ExtractedFieldSpec? spec = specs.FirstOrDefault(f => f.Key == key);
        if (spec is null)
        {
            return null;
        }

        AiFieldResult? field = result.Fields
            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        string? normalized = ExtractedFieldValidators.Normalize(spec.Type, field?.Value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        bool matches = ExtractedFieldValidators.Validate(spec.Type, normalized);

        return new ScannedValue(
            normalized,
            matches,
            ExtractedFieldValidators.EffectiveConfidence(matches, field?.Confidence ?? 0d));
    }

    /// <summary>Explicația e pentru cazul în care n-a ieșit nimic — altfel ecranul ar tăcea.</summary>
    private static string? NoteFor(DocumentAiAnalysisResult result, ScannedValue? plate, ScannedValue? vin)
    {
        if (plate is not null || vin is not null)
        {
            return null;
        }

        if (!result.MatchesExpectedType)
        {
            return string.IsNullOrWhiteSpace(result.DetectedType)
                ? "Documentul nu pare a fi un talon."
                : $"Documentul nu pare a fi un talon, ci {result.DetectedType}.";
        }

        return result.IsReadable
            ? "N-am găsit numărul de înmatriculare în document."
            : "Poza e prea neclară ca să pot citi din ea. Încearcă una mai apropiată, la lumină bună.";
    }
}

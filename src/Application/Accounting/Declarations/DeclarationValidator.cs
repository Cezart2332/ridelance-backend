using Application.Abstractions.Anaf;
using Application.Abstractions.Data;
using Application.Accounting.Contracts;
using Application.Accounting.Months;
using Application.Accounting.Tax;
using Domain.Accounting;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace Application.Accounting.Declarations;

/// <summary>Rezultatul salvat în <c>validation_result_json</c>: nivelurile, plus răspunsul brut ANAF.</summary>
internal sealed record StoredValidation(
    IReadOnlyList<ValidationLevelResult> Levels,
    DateTime ValidatedAt,
    string? SchemaVersion,
    string? ValidatorVersion,
    AnafRawResult? Anaf);

/// <summary>Răspunsul serviciului ANAF, fără PDF, sau motivul pentru care nu a putut fi apelat.</summary>
internal sealed record AnafRawResult(
    bool? Valid,
    IReadOnlyList<AnafValidatorMessage> Errors,
    IReadOnlyList<AnafValidatorMessage> Warnings,
    string? RawOutput,
    long? DurationMs,
    string? CorrelationId,
    string? Failure);

/// <summary>Ce a ieșit dintr-o validare: statusul nou și un mesaj pentru rezumatul jobului.</summary>
internal sealed record ValidationRun(DeclarationStatus Status, bool Passed, string Summary);

/// <summary>
/// Validarea pe 3 niveluri (spec contabilitate B4), doar din <c>GENERATED</c>:
/// <list type="number">
/// <item><b>RIDElance</b>: recalculul din snapshot dă același rezultat, liniile salvate și totalul
/// corespund, documentele lunii nu s-au schimbat după generare, antetul e complet, iar XML-ul citit
/// înapoi are sumele și sumele de control corecte;</item>
/// <item><b>XSD</b>: schema oficială a perioadei;</item>
/// <item><b>ANAF</b>: DUKIntegrator prin <c>ridelance-anaf-validator</c>, care dă și PDF-ul.</item>
/// </list>
/// Toate trec: PDF-ul se salvează și versiunea ajunge <c>VALIDATED</c>, apoi <c>READY_TO_SIGN</c>.
/// Un nivel picat: <c>VALIDATION_FAILED</c>. Serviciul ANAF indisponibil nu e o eroare a declarației:
/// statusul rămâne <c>GENERATED</c>, cu motivul în rezultat.
/// </summary>
internal sealed class DeclarationValidator(
    IApplicationDbContext db,
    IDeclarationXmlService xml,
    IAnafValidatorClient anaf,
    DeclarationFiles files,
    IOptions<AccountingOptions> options)
{
    public async Task<Result<ValidationRun>> ValidateAsync(Guid versionId, Guid? userId, CancellationToken cancellationToken)
    {
        DeclarationVersion? version = await db.DeclarationVersions
            .Include(v => v.Declaration)
            .Include(v => v.Lines.Where(line => line.SupersededAtUtc == null))
            .Include(v => v.Schema)
            .SingleOrDefaultAsync(v => v.Id == versionId, cancellationToken);
        if (version is null)
        {
            return Result.Failure<ValidationRun>(DeclarationErrors.VersionNotFound);
        }

        if (version.Status != DeclarationStatus.Generated)
        {
            return Result.Failure<ValidationRun>(DeclarationErrors.InvalidTransition(version.Status, DeclarationAction.Validate));
        }

        Declaration declaration = version.Declaration;
        if (version.Schema is null)
        {
            version.Schema = DeclarationFiles.PickSchema(
                await db.AnafDeclarationSchemas.Where(s => s.DeclarationType == declaration.Type).ToListAsync(cancellationToken),
                declaration.Type,
                declaration.Period);
            version.SchemaId = version.Schema?.Id;
        }

        AnafDeclarationSchema? schema = version.Schema;
        var snapshot = DeclarationSnapshot.Read(version.SnapshotJson);
        var ridelance = new List<ValidationMessage>();
        var xsd = new List<ValidationMessage>();
        byte[]? content = null;

        if (snapshot is null)
        {
            ridelance.Add(new ValidationMessage(null, "Snapshot-ul versiunii lipsește sau e deteriorat; calculul nu poate fi verificat."));
        }
        else
        {
            ridelance.AddRange(CheckCalculation(declaration.Type, version, snapshot));
            MonthData data = await MonthData.LoadAsync(db, declaration.Period, [declaration.PfaRegistrationId], cancellationToken);
            if (!SameInvoices(data.TaxInput(declaration.PfaRegistrationId, snapshot.Input.Settings).Invoices, snapshot.Input.Invoices))
            {
                ridelance.Add(new ValidationMessage(null, "Documentele lunii s-au schimbat după generare: declarația trebuie regenerată („Regenerează”)."));
            }
        }

        AnafTaxpayer? taxpayer = await files.TaxpayerAsync(declaration.PfaRegistrationId, cancellationToken);
        if (taxpayer is not null)
        {
            ridelance.AddRange(CheckTaxpayer(declaration.Type, taxpayer));
        }

        string? blocker = DeclarationContent.XmlBlocker(declaration.Type, version.Kind, options.Value.D100CorrectionProcedure);
        if (blocker is not null)
        {
            xsd.Add(new ValidationMessage(null, blocker));
        }
        else if (schema is null)
        {
            xsd.Add(new ValidationMessage(null, $"Nu există schemă ANAF {declaration.Type} valabilă pentru {declaration.Period} (Reguli fiscale → Scheme ANAF)."));
        }
        else if (!xml.Supports(declaration.Type, schema.Version))
        {
            xsd.Add(new ValidationMessage(null, $"Schema {declaration.Type} {schema.Version} nu are încă generator de XML."));
        }
        else if (snapshot is not null && taxpayer is not null)
        {
            content = (await files.ReadAsync(version.XmlDocumentId, cancellationToken))?.Content
                ?? await files.WriteXmlAsync(declaration, version, taxpayer, snapshot, schema, cancellationToken);
            if (content is not null)
            {
                ridelance.AddRange(xml.VerifyContent(schema.Version, DeclarationFiles.Input(declaration, version, taxpayer, snapshot), content));
                xsd.AddRange(schema.XsdPath is null
                    ? [new ValidationMessage(null, $"Schema {declaration.Type} {schema.Version} nu are fișier XSD.")]
                    : xml.ValidateSchema(schema.XsdPath, content));
            }
        }

        if (content is null && xsd.Count == 0)
        {
            xsd.Add(new ValidationMessage(null, "XML-ul nu a putut fi generat."));
        }

        (ValidationLevelResult Level, AnafRawResult? Raw, byte[]? Pdf, bool Unavailable) anafLevel =
            (new ValidationLevelResult(ValidationLevel.Anaf, false, [new ValidationMessage(null, "Nu s-a rulat: nivelurile anterioare au erori.")]), null, null, false);
        if (ridelance.Count == 0 && xsd.Count == 0)
        {
            anafLevel = await ValidateWithAnafAsync(declaration.Type, schema!, content!, version.Id, cancellationToken);
        }

        (ValidationLevelResult level3, AnafRawResult? raw, byte[]? pdf, bool unavailable) = anafLevel;

        ValidationLevelResult[] levels =
        [
            new(ValidationLevel.Ridelance, ridelance.Count == 0, ridelance),
            new(ValidationLevel.Xsd, xsd.Count == 0, xsd),
            level3,
        ];
        version.ValidationResultJson = AccountingJson.Serialize(new StoredValidation(levels, DateTime.UtcNow, schema?.Version, schema?.ValidatorVersion, raw));

        ValidationRun run;
        if (levels.All(level => level.Passed))
        {
            Domain.Documents.Document document = await files.StoreAsync(
                declaration.PfaRegistrationId,
                pdf!,
                $"{declaration.Type}_{taxpayer!.Cui}_{declaration.Period}_v{version.VersionNo}.pdf",
                "application/pdf",
                cancellationToken);
            version.PdfDocumentId = document.Id;
            DeclarationStateMachine.Move(version, DeclarationStatus.Validated, userId, "Validare RIDElance, XSD și ANAF trecută.");
            DeclarationStateMachine.Move(version, DeclarationStatus.ReadyToSign, userId, "PDF generat de DUKIntegrator.");
            run = new ValidationRun(version.Status, true, $"{declaration.Type}: pregătit pentru depunere.");
        }
        else if (unavailable)
        {
            run = new ValidationRun(version.Status, false, $"{declaration.Type}: {level3.Messages[0].Text}");
        }
        else
        {
            DeclarationStateMachine.Move(version, DeclarationStatus.ValidationFailed, userId, null);
            ValidationMessage first = levels.First(level => !level.Passed).Messages[0];
            run = new ValidationRun(version.Status, false, $"{declaration.Type}: {first.Text}");
        }

        AccountingAudit.Record(
            db, declaration.PfaRegistrationId, nameof(DeclarationVersion), version.Id, "VALIDATE", null,
            new { status = version.Status, levels = levels.Select(level => new { level.Level, level.Passed }) }, null, userId);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task<(ValidationLevelResult Level, AnafRawResult? Raw, byte[]? Pdf, bool Unavailable)> ValidateWithAnafAsync(
        DeclarationType type, AnafDeclarationSchema schema, byte[] content, Guid versionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(schema.ValidatorVersion))
        {
            return (Failed($"Schema {type} {schema.Version} nu are versiunea validatorului ANAF."), null, null, false);
        }

        Result<AnafValidatorResult> called = await anaf.ValidateAsync(type, schema.ValidatorVersion, content, versionId.ToString(), cancellationToken);
        if (called.IsFailure)
        {
            string message = $"Validatorul ANAF nu a putut fi folosit: {called.Error.Description} Încearcă din nou.";
            return (Failed(message), new AnafRawResult(null, [], [], null, null, versionId.ToString(), called.Error.Description), null, true);
        }

        AnafValidatorResult result = called.Value;
        var raw = new AnafRawResult(result.Valid, result.Errors, result.Warnings, result.RawOutput, result.DurationMs, result.CorrelationId, null);
        List<ValidationMessage> messages =
        [
            .. result.Errors.Select(error => new ValidationMessage(error.Field, Text(error))),
            .. result.Warnings.Select(warning => new ValidationMessage(warning.Field, $"Atenționare: {Text(warning)}")),
        ];

        if (!result.Valid)
        {
            if (messages.Count == 0)
            {
                messages.Add(new ValidationMessage(null, string.IsNullOrWhiteSpace(result.RawOutput) ? "Declarația nu a trecut validarea ANAF." : result.RawOutput));
            }

            return (new ValidationLevelResult(ValidationLevel.Anaf, false, messages), raw, null, false);
        }

        if (result.Pdf is not { Length: > 0 })
        {
            messages.Insert(0, new ValidationMessage(null, "Validatorul ANAF nu a întors PDF-ul declarației."));
            return (new ValidationLevelResult(ValidationLevel.Anaf, false, messages), raw, null, false);
        }

        return (new ValidationLevelResult(ValidationLevel.Anaf, true, messages), raw, result.Pdf, false);

        static ValidationLevelResult Failed(string text) => new(ValidationLevel.Anaf, false, [new ValidationMessage(null, text)]);

        static string Text(AnafValidatorMessage message) =>
            string.Join(" ", new[] { message.Code is null ? null : $"{message.Code}:", message.Message, message.Location is null ? null : $"({message.Location})" }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>Recalculul din snapshot, liniile salvate și totalul versiunii trebuie să coincidă.</summary>
    internal static List<ValidationMessage> CheckCalculation(DeclarationType type, DeclarationVersion version, DeclarationSnapshot snapshot)
    {
        var messages = new List<ValidationMessage>();
        DeclarationCalculation saved = snapshot.Calculation;
        TaxResult recalculated = MonthlyTaxEngine.Calculate(snapshot.Input);
        messages.AddRange(recalculated.BlockingReasons.Select(reason => new ValidationMessage(null, $"Recalculul din snapshot e blocat: {reason}")));

        DeclarationCalculation again = recalculated.Declarations[type];
        if (again.Total != saved.Total || !SameLines(again.Lines.Select(Key), saved.Lines.Select(Key)))
        {
            messages.Add(new ValidationMessage(null, $"Recalculul din snapshot dă {AccountingJson.Amount(again.Total)} lei, iar calculul salvat {AccountingJson.Amount(saved.Total)} lei."));
        }

        if (version.Amount != saved.Total)
        {
            messages.Add(new ValidationMessage("total", $"Suma versiunii ({AccountingJson.Amount(version.Amount)} lei) diferă de totalul calculat ({AccountingJson.Amount(saved.Total)} lei)."));
        }

        if (!SameLines(version.Lines.Where(line => line.SupersededAtUtc == null).Select(line => (line.RuleCode, line.SourceDocumentId, line.Base, line.Rate, line.Value, line.SupplierVatId)), saved.Lines.Select(Key)))
        {
            messages.Add(new ValidationMessage(null, "Liniile salvate ale declarației nu corespund calculului din snapshot."));
        }

        if (type != DeclarationType.D390)
        {
            decimal sum = saved.Lines.Sum(line => line.Value);
            bool wholeLei = snapshot.Input.Settings.Rounding.TryGetValue(type, out DeclarationRounding rounding) && rounding == DeclarationRounding.WholeLei;
            decimal expected = wholeLei ? Math.Round(sum, 0, MidpointRounding.AwayFromZero) : sum;
            if (expected != saved.Total)
            {
                messages.Add(new ValidationMessage("total", $"Suma liniilor ({AccountingJson.Amount(sum)} lei) nu dă totalul declarației ({AccountingJson.Amount(saved.Total)} lei)."));
            }
        }

        return messages;

        static (string, Guid, decimal, decimal?, decimal, string) Key(TaxLine line) =>
            (line.RuleCode, line.SourceDocumentId, line.Base, line.Rate, line.Value, line.SupplierVatId);
    }

    /// <summary>Antetul declarației: câmpurile obligatorii ANAF și CUI-ul valid.</summary>
    internal static List<ValidationMessage> CheckTaxpayer(DeclarationType type, AnafTaxpayer taxpayer)
    {
        var messages = new List<ValidationMessage>();
        if (!CuiValidator.Validate(taxpayer.Cui).IsValid)
        {
            messages.Add(new ValidationMessage("cui", $"CUI-ul PFA-ului („{taxpayer.Cui}”) nu e valid."));
        }

        if (string.IsNullOrWhiteSpace(taxpayer.Name))
        {
            messages.Add(new ValidationMessage("den", "Lipsește denumirea PFA-ului."));
        }

        if (string.IsNullOrWhiteSpace(taxpayer.Address))
        {
            messages.Add(new ValidationMessage("adresa", "Lipsește adresa (domiciliul fiscal) PFA-ului."));
        }

        if (string.IsNullOrWhiteSpace(taxpayer.DeclarantLastName) || string.IsNullOrWhiteSpace(taxpayer.DeclarantFirstName))
        {
            messages.Add(new ValidationMessage("nume_declar", "Lipsește numele sau prenumele titularului (declarantul)."));
        }

        if (type == DeclarationType.D301)
        {
            if (string.IsNullOrWhiteSpace(taxpayer.BankName))
            {
                messages.Add(new ValidationMessage("banca", "Lipsește banca PFA-ului (declarația contului bancar din onboarding)."));
            }

            if (string.IsNullOrWhiteSpace(taxpayer.Iban))
            {
                messages.Add(new ValidationMessage("cont", "Lipsește IBAN-ul PFA-ului (declarația contului bancar din onboarding)."));
            }
        }

        return messages;
    }

    /// <summary>Facturile din snapshot sunt cele de acum: aceleași documente, cu aceleași valori citite.</summary>
    private static bool SameInvoices(IEnumerable<TaxInvoice> current, IEnumerable<TaxInvoice> saved) =>
        SameLines(
            current.Select(i => (i.DocumentId, i.InvoiceNumber ?? string.Empty, i.InvoiceDate, i.Currency ?? string.Empty, i.CommissionAmount, i.SupplierVatId ?? string.Empty)),
            saved.Select(i => (i.DocumentId, i.InvoiceNumber ?? string.Empty, i.InvoiceDate, i.Currency ?? string.Empty, i.CommissionAmount, i.SupplierVatId ?? string.Empty)));

    private static bool SameLines<T>(IEnumerable<T> left, IEnumerable<T> right) =>
        left.Order().SequenceEqual(right.Order());
}

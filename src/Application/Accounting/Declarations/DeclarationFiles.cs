using System.Globalization;
using Application.Abstractions.Anaf;
using Application.Abstractions.Data;
using Application.Abstractions.Security;
using Application.Abstractions.Services;
using Application.Accounting.Documents;
using Application.Accounting.Months;
using Application.Accounting.Pfas;
using Application.Documents.ExtractedFields;
using Domain.Accounting;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Application.Accounting.Declarations;

/// <summary>
/// Fișierele unei versiuni de declarație (spec contabilitate B4): XML-ul generat din snapshot și
/// PDF-ul DUKIntegrator, stocate criptat ca documente ale PFA-ului, invizibile clientului.
/// </summary>
internal sealed class DeclarationFiles(
    IApplicationDbContext db,
    IDeclarationXmlService xml,
    IFileEncryptionService files,
    ISecretProtector secrets)
{
    /// <summary>Titularul PFA-ului, declarantul din antet.</summary>
    public const string DeclarantFunction = "TITULAR";

    /// <summary>Datele din antet, cu IBAN-ul decriptat; <c>null</c> dacă PFA-ul nu există.</summary>
    /// <param name="period">Luna declarației: codul de TVA art. 317 e cel valabil la sfârșitul ei.</param>
    public async Task<AnafTaxpayer?> TaxpayerAsync(Guid pfaId, CancellationToken cancellationToken, string? period = null)
    {
        var pfa = await db.PfaRegistrations
            .AsNoTracking()
            .Where(p => p.Id == pfaId)
            .Select(p => new
            {
                p.Cui,
                p.LegalName,
                p.FullName,
                p.HolderName,
                p.Street,
                p.Number,
                p.City,
                p.County,
                p.User.FirstName,
                p.User.LastName,
                BankName = p.BankAccountDeclaration == null ? null : p.BankAccountDeclaration.BankName,
                Iban = p.BankAccountDeclaration == null ? null : p.BankAccountDeclaration.IbanEncrypted,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (pfa is null)
        {
            return null;
        }

        (string lastName, string firstName) = Declarant(pfa.LastName, pfa.FirstName, pfa.HolderName ?? pfa.FullName);
        string street = string.Join(' ', new[] { pfa.Street, pfa.Number }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        string address = string.Join(", ", new[] { street, pfa.City, pfa.County }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));

        return new AnafTaxpayer(
            Cui: new string((pfa.Cui ?? string.Empty).Where(char.IsDigit).ToArray()),
            Name: PfaNames.Of(pfa.LegalName, pfa.HolderName, pfa.FullName, pfa.FirstName, pfa.LastName),
            Address: address,
            DeclarantLastName: lastName,
            DeclarantFirstName: firstName,
            DeclarantFunction: DeclarantFunction,
            BankName: pfa.BankName?.Trim(),
            Iban: SensitiveFieldProtection.TryUnprotect(secrets, pfa.Iban)?.Replace(" ", string.Empty, StringComparison.Ordinal),
            VatCode: await Art317VatCodeAsync(pfaId, period, cancellationToken));
    }

    private async Task<string?> Art317VatCodeAsync(Guid pfaId, string? period, CancellationToken cancellationToken)
    {
        DateOnly at = period is not null && PlatformDocumentSupport.IsValidPeriod(period)
            ? AccountingScope.Bounds(period).End
            : DateOnly.FromDateTime(DateTime.UtcNow);
        string? json = await db.PfaAccountingSettings.AsNoTracking()
            .Where(s => s.PfaRegistrationId == pfaId && s.Key == PfaAccountingSettingKeys.Art317VatCode && s.ValidFrom <= at)
            .OrderByDescending(s => s.ValidFrom)
            .Select(s => s.ValueJson)
            .FirstOrDefaultAsync(cancellationToken);
        return json is null ? null : AccountingJson.Deserialize<string?>(json, null);
    }

    /// <summary>Intrarea mapperului: liniile calculului, cu numărul și data facturii din snapshot.</summary>
    public static AnafDeclarationInput Input(Declaration declaration, DeclarationVersion version, AnafTaxpayer taxpayer, DeclarationSnapshot snapshot, Tax.DeclarationRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var invoices = snapshot.Input.Invoices.ToDictionary(invoice => invoice.DocumentId);
        return new AnafDeclarationInput(
            declaration.Type,
            declaration.Period,
            version.Kind == DeclarationVersionKind.Rectificative,
            taxpayer,
            [.. snapshot.Calculation.Lines.Select(line =>
            {
                Tax.TaxInvoice? invoice = invoices.GetValueOrDefault(line.SourceDocumentId);
                return new AnafDeclarationLine(
                    invoice?.InvoiceNumber,
                    invoice?.InvoiceDate,
                    line.AmountInCurrency,
                    line.Currency,
                    line.ExchangeRate,
                    line.Base,
                    line.Value,
                    line.SupplierName,
                    line.SupplierCountry,
                    line.SupplierVatId);
            })],
            version.Amount,
            declaration.Type == DeclarationType.D390 || rules.ObligationCode is not null
                ? rules.ObligationCode
                : throw new Tax.TaxRuleConfigurationException($"Lipsește codul de obligație {declaration.Type} pentru {declaration.Period}."),
            rules.BudgetCode,
            rules.DueDate ?? throw new Tax.TaxRuleConfigurationException($"Lipsește termenul {declaration.Type} pentru {declaration.Period}."));
    }

    /// <summary>
    /// Codurile și termenul declarației: cele înghețate în snapshot la generare; un snapshot mai vechi
    /// decât regulile versionate le ia din regulile perioadei.
    /// </summary>
    public async Task<Tax.DeclarationRules> RulesAsync(Declaration declaration, DeclarationSnapshot snapshot, CancellationToken cancellationToken) =>
        snapshot.Input.Settings.Declarations?.GetValueOrDefault(declaration.Type) is { DueDate: not null } frozen
            ? frozen
            : Tax.TaxEngineSettings.ForPeriod(await Tax.TaxRuleSet.LoadAsync(db, cancellationToken), declaration.Period).Declarations![declaration.Type];

    /// <summary>SHA-256 al XML-ului, în hex: aceleași date și reguli dau același hash.</summary>
    public static string Hash(byte[] content) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));

    /// <summary>Schema valabilă la sfârșitul perioadei declarației (nu la data curentă).</summary>
    public static AnafDeclarationSchema? PickSchema(IEnumerable<AnafDeclarationSchema> schemas, DeclarationType type, string period)
    {
        DateOnly end = PeriodEnd(period);
        return schemas
            .Where(s => s.DeclarationType == type && s.ValidFrom <= end && (s.ValidTo == null || s.ValidTo >= end))
            .OrderByDescending(s => s.ValidFrom)
            .FirstOrDefault();
    }

    public static DateOnly PeriodEnd(string period)
    {
        var start = DateOnly.ParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return start.AddMonths(1).AddDays(-1);
    }

    /// <summary>
    /// Generează și salvează XML-ul versiunii. Întoarce conținutul, sau <c>null</c> dacă nu există
    /// schemă sau mapper pentru perioadă (nivelul 2 o raportează).
    /// </summary>
    public async Task<byte[]?> WriteXmlAsync(
        Declaration declaration,
        DeclarationVersion version,
        AnafTaxpayer taxpayer,
        DeclarationSnapshot snapshot,
        AnafDeclarationSchema? schema,
        CancellationToken cancellationToken)
    {
        if (schema is null || !xml.Supports(declaration.Type, schema.Version))
        {
            return null;
        }

        byte[] content = xml.Build(schema.Version, Input(declaration, version, taxpayer, snapshot, await RulesAsync(declaration, snapshot, cancellationToken)));
        version.XmlHash = Hash(content);
        version.RulesetVersion = snapshot.Input.Settings.RulesetVersion;
        Document document = await StoreAsync(
            declaration.PfaRegistrationId,
            content,
            $"{declaration.Type}_{taxpayer.Cui}_{declaration.Period}_v{version.VersionNo}.xml",
            "application/xml",
            cancellationToken);
        version.XmlDocumentId = document.Id;
        return content;
    }

    public async Task<Document> StoreAsync(
        Guid pfaId,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken,
        DocumentOrigin origin = DocumentOrigin.AccountingGenerated)
    {
        Guid userId = await db.PfaRegistrations.Where(p => p.Id == pfaId).Select(p => p.UserId).SingleAsync(cancellationToken);
        string storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(fileName)}";
        EncryptedFileResult encrypted;
        using (var stream = new MemoryStream(content))
        {
            encrypted = await files.EncryptAndSaveAsync(stream, storedFileName, cancellationToken);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PfaRegistrationId = pfaId,
            OriginalFileName = fileName,
            StoredFileName = storedFileName,
            ContentType = contentType,
            Category = DocumentCategory.Other,
            Status = DocumentStatus.Verified,
            Origin = origin,
            EncryptedFilePath = encrypted.FilePath,
            EncryptionIv = encrypted.Iv,
            FileSize = content.Length,
            UploadedAtUtc = DateTime.UtcNow,
            AiStatus = DocumentAiStatus.None,
        };
        db.Documents.Add(document);
        return document;
    }

    /// <summary>Conținutul unui document stocat, sau <c>null</c> dacă nu există.</summary>
    public async Task<(byte[] Content, Document Document)?> ReadAsync(Guid? documentId, CancellationToken cancellationToken)
    {
        if (documentId is not { } id || await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken) is not Document document)
        {
            return null;
        }

        await using Stream stream = await files.DecryptAndReadAsync(document.EncryptedFilePath, document.EncryptionIv, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return (buffer.ToArray(), document);
    }

    /// <summary>Numele și prenumele declarantului: din cont, altfel din numele titularului („Prenume Nume”).</summary>
    private static (string LastName, string FirstName) Declarant(string? lastName, string? firstName, string? fullName)
    {
        if (!string.IsNullOrWhiteSpace(lastName) && !string.IsNullOrWhiteSpace(firstName))
        {
            return (lastName.Trim(), firstName.Trim());
        }

        string[] parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => (lastName?.Trim() ?? string.Empty, firstName?.Trim() ?? string.Empty),
            1 => (parts[0], firstName?.Trim() ?? string.Empty),
            _ => (parts[^1], string.Join(' ', parts[..^1])),
        };
    }
}

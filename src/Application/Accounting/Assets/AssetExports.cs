using System.Globalization;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Accounting.Contracts;
using Application.Accounting.Ledger;
using Application.Accounting.Registers;
using Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Accounting.Assets;

/// <summary><c>GET /accounting/pfas/{pfaId}/assets/{id}/sheet?format</c> — Fișa mijlocului fix.</summary>
public sealed record ExportAssetSheetQuery(Guid PfaId, Guid AssetId, RegisterFormat Format) : IQuery<RegisterFile>;

/// <summary>
/// Fișa mijlocului fix (model 14-2-2, OMFP 2634/2015), generată doar pentru un mijloc fix: numărul de
/// inventar, denumirea, documentul de achiziție, datele de intrare și de punere în funcțiune, valoarea
/// de intrare, clasa, durata, metoda și planul lunar (amortizare, cumulată, valoare rămasă).
/// </summary>
internal sealed class ExportAssetSheetQueryHandler(IApplicationDbContext db, IRegisterExporter exporter, IDateTimeProvider? clock = null)
    : IQueryHandler<ExportAssetSheetQuery, RegisterFile>
{
    public async Task<Result<RegisterFile>> Handle(ExportAssetSheetQuery query, CancellationToken cancellationToken)
    {
        List<AssetDto> assets = await AssetSupport.DtosAsync(
            db, db.PfaAssets.Where(a => a.Id == query.AssetId && a.PfaRegistrationId == query.PfaId && a.Kind == AssetKind.FixedAsset), AssetSupport.Today(clock), cancellationToken);
        if (assets.Count == 0 || await RegisterData.PfaAsync(db, query.PfaId, cancellationToken) is not { } pfa)
        {
            return Result.Failure<RegisterFile>(AssetErrors.NotFound);
        }

        AssetDto asset = assets[0];
        List<DepreciationLine> lines = await db.DepreciationLines.AsNoTracking()
            .Where(l => l.AssetId == asset.Id)
            .OrderBy(l => l.Year).ThenBy(l => l.Month)
            .ToListAsync(cancellationToken);

        var document = new RegisterDocument(
            "FIȘA MIJLOCULUI FIX",
            "14-2-2",
            [
                $"{pfa.Name} — CUI {pfa.Cui}",
                $"Nr. inventar {asset.InventoryNumber} — {asset.Name}",
                $"Document de achiziție: {asset.DocumentRef}{(asset.SupplierName is { Length: > 0 } supplier ? $", furnizor {supplier}" : string.Empty)}",
                $"Data intrării: {RegisterData.Date(asset.EntryDate)}; punere în funcțiune: {(asset.InServiceDate is { } inService ? RegisterData.Date(inService) : "—")}",
                $"Valoare de intrare: {RegisterData.Amount(asset.EntryValue)} lei; clasa {asset.DepreciationClassCode ?? "—"}; durata normală {asset.NormalLifeMonths?.ToString(CultureInfo.InvariantCulture) ?? "—"} luni; metoda liniară",
                asset.DisposalDate is { } disposal ? $"Ieșit din gestiune la {RegisterData.Date(disposal)}: {asset.DisposalReason}" : "În folosință",
            ],
            [
                new("Nr. crt.", Width: 0.5f),
                new("Luna", Width: 1.2f),
                new("Amortizare lunară", Numeric: true),
                new("Amortizare cumulată", Numeric: true),
                new("Valoare rămasă", Numeric: true),
            ],
            null,
            [.. lines.Select((line, index) => new RegisterLine(
                [index + 1, $"{line.Month:00}.{line.Year}", line.Amount, line.Accumulated, line.Remaining]))],
            ["Model conform OMFP nr. 2634/2015. Amortizarea începe în luna următoare punerii în funcțiune; diferența de rotunjire e în ultima lună."]);
        return RegisterFiles.Export(exporter, document, query.Format, $"Fisa_MF_{asset.InventoryNumber}_{pfa.Cui}");
    }
}

/// <summary><c>GET /accounting/pfas/{pfaId}/assets/register?asOf&amp;format</c> — lista activelor.</summary>
public sealed record ExportAssetListQuery(Guid PfaId, DateOnly? AsOf, RegisterFormat Format) : IQuery<RegisterFile>;

/// <summary>Registrul imobilizărilor: fiecare activ, cu amortizarea cumulată și valoarea rămasă la o dată.</summary>
internal sealed class ExportAssetListQueryHandler(IApplicationDbContext db, IRegisterExporter exporter, IDateTimeProvider? clock = null)
    : IQueryHandler<ExportAssetListQuery, RegisterFile>
{
    public async Task<Result<RegisterFile>> Handle(ExportAssetListQuery query, CancellationToken cancellationToken)
    {
        if (await RegisterData.PfaAsync(db, query.PfaId, cancellationToken) is not { } pfa)
        {
            return Result.Failure<RegisterFile>(AccountingErrors.PfaNotFound);
        }

        DateOnly asOf = query.AsOf ?? AssetSupport.Today(clock);
        List<AssetDto> assets = await AssetSupport.DtosAsync(
            db, db.PfaAssets.Where(a => a.PfaRegistrationId == query.PfaId && a.EntryDate <= asOf), asOf, cancellationToken);
        List<RegisterLine> lines = [.. assets.Select(asset => new RegisterLine(
        [
            asset.InventoryNumber,
            asset.Name,
            asset.Kind == AssetKind.FixedAsset ? "Mijloc fix" : "Obiect de inventar",
            RegisterData.Date(asset.EntryDate),
            asset.InServiceDate is { } inService ? RegisterData.Date(inService) : null,
            asset.EntryValue,
            asset.NormalLifeMonths,
            asset.Accumulated,
            asset.Remaining,
            Status(asset.Status),
        ]))];
        lines.Add(new RegisterLine([null, "Total", null, null, null, assets.Sum(a => a.EntryValue), null, assets.Sum(a => a.Accumulated), assets.Sum(a => a.Remaining), null], Emphasis: true));

        var document = new RegisterDocument(
            "LISTA ACTIVELOR (REGISTRUL IMOBILIZĂRILOR)",
            "—",
            [$"{pfa.Name} — CUI {pfa.Cui}", $"la data de {RegisterData.Date(asOf)}", "Sume în lei"],
            [
                new("Nr. inventar", Width: 0.8f),
                new("Denumire", Width: 2.2f),
                new("Fel", Width: 1f),
                new("Data intrării", Width: 0.9f),
                new("Punere în funcțiune", Width: 0.9f),
                new("Valoare de intrare", Numeric: true),
                new("Durată (luni)", Numeric: true, Width: 0.6f),
                new("Amortizare cumulată", Numeric: true),
                new("Valoare rămasă", Numeric: true),
                new("Stare", Width: 1f),
            ],
            null,
            lines,
            ["Obiectele de inventar nu se amortizează: valoarea lor s-a dedus la achiziție."]);
        return RegisterFiles.Export(exporter, document, query.Format, $"Lista_active_{pfa.Cui}_{asOf:yyyyMMdd}");
    }

    private static string Status(AssetStatus status) => status switch
    {
        AssetStatus.Active => "Activ",
        AssetStatus.PendingClassification => "De clasificat",
        AssetStatus.FullyDepreciated => "Amortizat integral",
        _ => "Ieșit din gestiune",
    };
}

// ─── PFA: activele, doar citire (spec registre §8) ───────────────────────────────────────────────

/// <summary><c>GET /pfa/assets</c> — lista activelor PFA-ului logat.</summary>
public sealed record GetClientAssetsQuery : IQuery<IReadOnlyList<AssetDto>>;

internal sealed class GetClientAssetsQueryHandler(IApplicationDbContext db, IUserContext userContext, IDateTimeProvider? clock = null)
    : IQueryHandler<GetClientAssetsQuery, IReadOnlyList<AssetDto>>
{
    public async Task<Result<IReadOnlyList<AssetDto>>> Handle(GetClientAssetsQuery query, CancellationToken cancellationToken) =>
        await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is { } pfaId
            ? await AssetSupport.DtosAsync(db, db.PfaAssets.Where(a => a.PfaRegistrationId == pfaId), AssetSupport.Today(clock), cancellationToken)
            : Result.Failure<IReadOnlyList<AssetDto>>(ClientLedger.NoPfa);
}

/// <summary><c>GET /pfa/assets/{id}/sheet</c> — Fișa MF a unui activ al PFA-ului logat.</summary>
public sealed record GetClientAssetSheetQuery(Guid AssetId) : IQuery<RegisterFile>;

internal sealed class GetClientAssetSheetQueryHandler(IApplicationDbContext db, IUserContext userContext, IQueryHandler<ExportAssetSheetQuery, RegisterFile> sheet)
    : IQueryHandler<GetClientAssetSheetQuery, RegisterFile>
{
    public async Task<Result<RegisterFile>> Handle(GetClientAssetSheetQuery query, CancellationToken cancellationToken) =>
        await ClientLedger.PfaIdAsync(db, userContext.UserId, cancellationToken) is { } pfaId
            ? await sheet.Handle(new ExportAssetSheetQuery(pfaId, query.AssetId, RegisterFormat.Pdf), cancellationToken)
            : Result.Failure<RegisterFile>(ClientLedger.NoPfa);
}

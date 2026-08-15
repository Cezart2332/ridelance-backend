using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.AiVerification;
using Application.Documents.Expiry;
using Application.Documents.Registry;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.Overview;

/// <param name="Status">
/// Starea afișată utilizatorului, deja decisă pe server: `Lipsa`, `Valid`, `ExpiraCurand`,
/// `Expirat`, plus `InVerificare` și `Respins` pentru documentele existente dar nevalidate.
/// </param>
public sealed record DocumentOverviewItem(
    string Key,
    string Label,
    bool HasIssueDate,
    bool HasExpiryDate,
    bool IsOptional,
    string Status,
    Guid? DocumentId,
    string? OriginalFileName,
    string? ContentType,
    DateTime? UploadedAtUtc,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    int? DaysUntilExpiry);

public sealed record DocumentsOverviewResponse(string Group, List<DocumentOverviewItem> Items);

public sealed record GetDocumentsOverviewQuery(string? Group) : IQuery<DocumentsOverviewResponse>;

internal sealed class GetDocumentsOverviewQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetDocumentsOverviewQuery, DocumentsOverviewResponse>
{
    public async Task<Result<DocumentsOverviewResponse>> Handle(
        GetDocumentsOverviewQuery query,
        CancellationToken cancellationToken)
    {
        if (!DocumentRegistry.TryParseGroup(query.Group, out DocumentGroup group))
        {
            return Result.Failure<DocumentsOverviewResponse>(
                Error.Problem("Documents.InvalidGroup", "Grupul de documente cerut nu există."));
        }

        Guid userId = userContext.UserId;
        IReadOnlyList<DocumentTypeDef> definitions = DocumentRegistry.ForGroup(group);
        var categories = definitions.SelectMany(d => d.Categories).ToHashSet();

        // Un singur tur la bază pentru tot grupul. Documentele respinse rămân în listă: userul
        // trebuie să vadă că a încărcat ceva și că nu a fost acceptat, nu un „Lipsește" derutant.
        List<Document> documents = await context.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId && categories.Contains(d.Category))
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(cancellationToken);

        DateOnly today = DocumentDateValidator.TodayInRomania();

        var items = definitions
            .Select(def => Build(def, documents, today))
            .ToList();

        return new DocumentsOverviewResponse(group.ToString(), items);
    }

    private static DocumentOverviewItem Build(
        DocumentTypeDef def,
        List<Document> documents,
        DateOnly today)
    {
        // Cel mai recent document al tipului. Înlocuirile păstrează istoricul în bază, dar pe
        // ecran contează actul curent.
        Document? current = documents
            .Where(d => def.Categories.Contains(d.Category))
            .OrderByDescending(d => d.UploadedAtUtc)
            .FirstOrDefault();

        if (current is null)
        {
            return new DocumentOverviewItem(
                def.Key, def.Label, def.HasIssueDate, def.HasExpiryDate, def.IsOptional,
                Status: "Lipsa",
                DocumentId: null, OriginalFileName: null, ContentType: null, UploadedAtUtc: null,
                IssuedOn: null, ExpiresOn: null, DaysUntilExpiry: null);
        }

        DocumentExpiry expiry = DocumentExpiryPolicy.Evaluate(current.Category, current.ExpiresAtUtc, today);

        // Expirarea bate starea de verificare: un act verificat, dar expirat, e tot expirat.
        string status = expiry.State switch
        {
            DocumentExpiryState.Expired => "Expirat",
            DocumentExpiryState.ExpiringSoon => "ExpiraCurand",
            _ => current.Status switch
            {
                DocumentStatus.Rejected => "Respins",
                DocumentStatus.Verified => "Valid",
                _ => "InVerificare",
            },
        };

        return new DocumentOverviewItem(
            def.Key, def.Label, def.HasIssueDate, def.HasExpiryDate, def.IsOptional,
            status,
            current.Id,
            current.OriginalFileName,
            current.ContentType,
            current.UploadedAtUtc,
            current.IssuedAtUtc is null ? null : DateOnly.FromDateTime(current.IssuedAtUtc.Value),
            expiry.ExpiresOn,
            expiry.DaysUntilExpiry);
    }
}

using Application.Abstractions.Security;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Documents.ExtractedFields;

/// <summary>Câmpurile extrase ale unui document, pentru admin (fără verificarea de proprietar).</summary>
public sealed record GetExtractedFieldsAdminQuery(Guid DocumentId) : IQuery<ExtractedFieldsResponse>;

/// <summary>
/// Câmpurile extrase dintr-un document, pentru admin — cu valorile reale, nu mascate.
///
/// Clientul și contabilul văd câmpurile sensibile (CNP, seria și numărul actului) ca `••••1234`.
/// Adminul le verifică pe act, deci are nevoie de ele întregi: cu masca, nu putea compara ce a
/// citit modelul cu ce scrie pe buletin. Valoarea reală se decriptează aici, doar pe acest drum,
/// iar endpointul e deja închis pe permisiunea de admin.
/// </summary>
internal sealed class GetExtractedFieldsAdminQueryHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector)
    : IQueryHandler<GetExtractedFieldsAdminQuery, ExtractedFieldsResponse>
{
    public async Task<Result<ExtractedFieldsResponse>> Handle(
        GetExtractedFieldsAdminQuery query,
        CancellationToken cancellationToken)
    {
        Document? document = await context.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == query.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<ExtractedFieldsResponse>(DocumentErrors.NotFound(query.DocumentId));
        }

        List<ExtractedField> fields = await context.ExtractedFields
            .AsNoTracking()
            .Where(f => f.DocumentId == document.Id)
            .OrderBy(f => f.FieldKey)
            .ToListAsync(cancellationToken);

        var dtos = fields.Select(field => Revealed(field)).ToList();

        return Result.Success(new ExtractedFieldsResponse(
            document.Id,
            document.Category.ToString(),
            document.AiConfidence,
            document.AiRequiresManualReview,
            dtos));
    }

    private ExtractedFieldDto Revealed(ExtractedField field)
    {
        ExtractedFieldDto masked = ExtractedFieldMapper.ToDto(field);

        if (!field.IsSensitive)
        {
            return masked;
        }

        string? real = SensitiveFieldProtection.Reveal(field, secretProtector);
        if (real is null)
        {
            return masked;
        }

        // Valoarea confirmată de om bate citirea automată, ca în restul aplicației.
        return masked with
        {
            AiValue = masked.AiValue is null ? null : real,
            ConfirmedValue = masked.ConfirmedValue is null ? null : real,
            EffectiveValue = real,
        };
    }
}

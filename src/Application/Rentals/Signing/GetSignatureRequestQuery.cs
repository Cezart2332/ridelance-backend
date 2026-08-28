using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Signing;

/// <summary>Ce vede cineva care deschide linkul din email. Fără cont, fără sesiune.</summary>
public sealed record GetSignatureRequestQuery(string Token) : IQuery<SignatureRequestDto>;

/// <param name="DocumentId">Fișierul de citit înainte de semnare. Se descarcă prin același token.</param>
public sealed record SignatureRequestDto(
    string DocumentTitle,
    string RentalCode,
    string CompanyName,
    string TenantName,
    Guid DocumentId,
    DateTime ExpiresAtUtc);

internal sealed class GetSignatureRequestQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetSignatureRequestQuery, SignatureRequestDto>
{
    public async Task<Result<SignatureRequestDto>> Handle(
        GetSignatureRequestQuery query,
        CancellationToken cancellationToken)
    {
        Result<SignatureRequest> found = await SignatureRequestLookup.FindAsync(
            context, query.Token, cancellationToken);

        if (found.IsFailure)
        {
            return Result.Failure<SignatureRequestDto>(found.Error);
        }

        SignatureRequest request = found.Value;
        Domain.Companies.CompanyProfile? company = await context.CompanyProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == request.GeneratedDocument.Rental.OwnerUserId, cancellationToken);

        return Result.Success(new SignatureRequestDto(
            SignatureRequestLookup.Label(request.GeneratedDocument.Type),
            request.GeneratedDocument.Rental.PublicCode,
            company?.LegalName ?? "RIDElance",
            request.GeneratedDocument.Rental.Tenant.Name,
            request.GeneratedDocument.DocumentId,
            request.ExpiresAtUtc));
    }
}

/// <summary>
/// Găsirea cererii după token, cu toate motivele de refuz într-un singur loc.
/// </summary>
/// <remarks>
/// Verificarea e aceeași la citire și la semnare. Scrisă de două ori, ar fi ajuns să difere — iar
/// varianta mai permisivă ar fi fost exact cea care lasă pe cineva să semneze cu un link expirat.
/// </remarks>
internal static class SignatureRequestLookup
{
    public static async Task<Result<SignatureRequest>> FindAsync(
        IApplicationDbContext context,
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Result.Failure<SignatureRequest>(Error.NotFound("Signature.NotFound", "Link invalid."));
        }

        string hash = SignatureToken.Hash(token);

        SignatureRequest? request = await context.SignatureRequests
            .Include(r => r.GeneratedDocument)
            .ThenInclude(d => d.Rental)
            .ThenInclude(r => r.Tenant)
            .FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);

        if (request is null)
        {
            return Result.Failure<SignatureRequest>(Error.NotFound("Signature.NotFound", "Link invalid."));
        }

        if (request.UsedAtUtc is not null)
        {
            return Result.Failure<SignatureRequest>(Error.Problem(
                "Signature.AlreadyUsed",
                "Documentul a fost deja semnat prin acest link."));
        }

        if (request.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Result.Failure<SignatureRequest>(Error.Problem(
                "Signature.Expired",
                "Linkul a expirat. Cere-i proprietarului să-l retrimită."));
        }

        return Result.Success(request);
    }

    public static string Label(string type) => type switch
    {
        "RentalContract" => "Contract de închiriere",
        "HandoverProtocol" => "Proces-verbal de predare",
        _ => "Proces-verbal de primire",
    };
}

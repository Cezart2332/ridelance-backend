using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaConnections;

/// <summary>
/// Conexiunea OBLIO, așa cum o vede titularul PFA-ului.
///
/// Atenție la ce NU e aici: configurația <c>Oblio</c> din appsettings e contul de facturare
/// al RIDElance (seria RMS, CIF-ul RIDElance), din care se emit facturile <em>către</em>
/// clienți. <c>IOblioService.TestConnectionAsync</c> ar întoarce firma RIDElance — a o afișa
/// pe pagina clientului ar fi pur și simplu greșit. Ce are clientul e contul lui Oblio,
/// modelat în <see cref="PfaOblioAccount"/>, plus datele propriului PFA.
/// </summary>
public sealed record OblioConnectionResponse(
    /// <summary>Pending | Requested | Active — starea integrării contului clientului.</summary>
    string Status,
    bool Connected,
    string? AccountEmail,
    string? CompanyName,
    string? Cui,
    bool ConsentsAccepted,
    DateTime? ConsentsAcceptedAtUtc,
    /// <summary>Nu există încă sincronizare per client; câmpul există pentru când va exista.</summary>
    DateTime? LastSyncAtUtc);

public sealed record GetOblioConnectionQuery : IQuery<OblioConnectionResponse>;

internal sealed class GetOblioConnectionQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetOblioConnectionQuery, OblioConnectionResponse>
{
    public async Task<Result<OblioConnectionResponse>> Handle(
        GetOblioConnectionQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .Include(p => p.OblioAccount)
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<OblioConnectionResponse>(
                Error.NotFound("PfaRegistration.NotFound", "Nu există un PFA pentru acest cont."));
        }

        PfaOblioAccount? account = pfa.OblioAccount;

        return new OblioConnectionResponse(
            Status: (account?.IntegrationStatus ?? OblioIntegrationStatus.Pending).ToString(),
            Connected: account?.IntegrationStatus == OblioIntegrationStatus.Active,
            AccountEmail: account?.AccountEmail,
            CompanyName: pfa.LegalName ?? pfa.FullName,
            Cui: pfa.Cui,
            ConsentsAccepted: account?.AllConsentsAccepted ?? false,
            ConsentsAcceptedAtUtc: account?.ConsentsAcceptedAtUtc,
            LastSyncAtUtc: null);
    }
}

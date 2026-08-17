using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>Starea dosarului de înființare al userului curent — pentru reluare de unde a rămas.</summary>
public sealed record GetCompanyFormationQuery(Guid UserId) : IQuery<CompanyFormationResponse>;

internal sealed class GetCompanyFormationQueryHandler(
    IApplicationDbContext context,
    CompanyFormationPrefillService prefill,
    ISecretProtector secretProtector)
    : IQueryHandler<GetCompanyFormationQuery, CompanyFormationResponse>
{
    public async Task<Result<CompanyFormationResponse>> Handle(
        GetCompanyFormationQuery query,
        CancellationToken cancellationToken)
    {
        // Buletinul se încarcă înainte să existe dosarul, deci datele citite din el n-au unde
        // ateriza la momentul OCR-ului. Le reluăm aici, la prima deschidere a formularului —
        // altfel utilizatorul vede câmpuri goale deși documentul a fost citit corect.
        await prefill.BackfillFromIdentityDocumentAsync(query.UserId, cancellationToken);

        PfaRegistration? registration = await context.PfaRegistrations
            .AsNoTracking()
            .Where(r => r.UserId == query.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Success(CompanyFormationMapper.Empty());
        }

        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .AsNoTracking()
            .Include(r => r.Owners)
            .Include(r => r.Signature)
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == registration.Id, cancellationToken);

        // Proprietarul dosarului își vede propriul CNP: altfel n-ar putea verifica ce a citit OCR-ul.
        return Result.Success(request is null
            ? CompanyFormationMapper.Empty()
            : CompanyFormationMapper.ToResponse(request, secretProtector, revealCnp: true));
    }
}

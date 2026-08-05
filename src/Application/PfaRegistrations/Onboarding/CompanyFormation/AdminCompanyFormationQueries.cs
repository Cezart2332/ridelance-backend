using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

public sealed record ConsentAuditDto(
    string StepKey,
    string Version,
    string TextSnapshot,
    string CheckboxLabelSnapshot,
    DateTime AcceptedAtUtc);

public sealed record SignatureAuditDto(
    DateTime SignedAtUtc,
    Guid? ImageDocumentId,
    string? IpAddress,
    string? UserAgent,
    string? DeviceType,
    string? Os,
    string? Browser,
    string PayloadHash);

/// <summary>
/// Dosarul așa cum îl vede operatorul: CNP-ul doar mascat, plus probatoriul semnăturii.
/// Valoarea în clar se cere separat, cu <see cref="RevealCompanyFormationCnpCommand"/>.
/// </summary>
public sealed record AdminCompanyFormationResponse(
    CompanyFormationResponse Dosar,
    IReadOnlyList<ConsentAuditDto> Consents,
    SignatureAuditDto? SignatureAudit);

public sealed record GetAdminCompanyFormationQuery(Guid PfaRegistrationId)
    : IQuery<AdminCompanyFormationResponse>;

internal sealed class GetAdminCompanyFormationQueryHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector)
    : IQueryHandler<GetAdminCompanyFormationQuery, AdminCompanyFormationResponse>
{
    public async Task<Result<AdminCompanyFormationResponse>> Handle(
        GetAdminCompanyFormationQuery query,
        CancellationToken cancellationToken)
    {
        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .AsNoTracking()
            .Include(r => r.Owners)
            .Include(r => r.Consents)
            .Include(r => r.Signature)
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == query.PfaRegistrationId, cancellationToken);

        if (request is null)
        {
            return Result.Failure<AdminCompanyFormationResponse>(CompanyFormationErrors.NoRegistration);
        }

        CompanyFormationSignature? signature = request.Signature;

        return Result.Success(new AdminCompanyFormationResponse(
            // revealCnp: false — operatorul vede masca, nu valoarea.
            CompanyFormationMapper.ToResponse(request, secretProtector, revealCnp: false),
            request.Consents
                .OrderBy(c => c.AcceptedAtUtc)
                .Select(c => new ConsentAuditDto(
                    c.StepKey, c.Version, c.TextSnapshot, c.CheckboxLabelSnapshot, c.AcceptedAtUtc))
                .ToList(),
            signature is null
                ? null
                : new SignatureAuditDto(
                    signature.SignedAtUtc,
                    signature.ImageDocumentId,
                    signature.IpAddress,
                    signature.UserAgent,
                    signature.DeviceType,
                    signature.Os,
                    signature.Browser,
                    signature.PayloadHash)));
    }
}

public sealed record RevealedCnpResponse(string Cnp);

/// <summary>
/// Dezvăluirea CNP-ului unei persoane din dosar. E o comandă, nu un query: fiecare citire lasă
/// urmă în jurnalul de activitate al dosarului (spec §6).
/// </summary>
public sealed record RevealCompanyFormationCnpCommand(
    Guid PfaRegistrationId,
    Guid? OwnerId,
    Guid RequestedByUserId)
    : ICommand<RevealedCnpResponse>;

internal sealed class RevealCompanyFormationCnpCommandHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector)
    : ICommandHandler<RevealCompanyFormationCnpCommand, RevealedCnpResponse>
{
    public async Task<Result<RevealedCnpResponse>> Handle(
        RevealCompanyFormationCnpCommand command,
        CancellationToken cancellationToken)
    {
        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .AsNoTracking()
            .Include(r => r.Owners)
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == command.PfaRegistrationId, cancellationToken);

        if (request is null)
        {
            return Result.Failure<RevealedCnpResponse>(CompanyFormationErrors.NoRegistration);
        }

        PersoanaFizica? persoana = request.Solicitant;
        string who = "solicitantului";

        if (command.OwnerId is Guid ownerId)
        {
            CompanyFormationOwner? owner = request.Owners.Find(o => o.Id == ownerId);
            if (owner is null)
            {
                return Result.Failure<RevealedCnpResponse>(CompanyFormationErrors.OwnerNotFound);
            }

            persoana = owner.Persoana;
            who = $"proprietarului {owner.Position + 1}";
        }

        if (string.IsNullOrWhiteSpace(persoana.CnpEncrypted))
        {
            return Result.Failure<RevealedCnpResponse>(CompanyFormationErrors.CnpMissing);
        }

        // Jurnalul se scrie înainte de a întoarce valoarea: dacă salvarea eșuează, CNP-ul
        // nu iese din sistem fără urmă.
        context.PfaActivityLogs.Add(new Domain.PfaRegistrations.PfaActivityLog
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaRegistrationId,
            ActivityType = "CompanyFormation.CnpRevealed",
            Description = $"A vizualizat CNP-ul {who} din dosarul de înființare.",
            PerformedByUserId = command.RequestedByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new RevealedCnpResponse(secretProtector.Unprotect(persoana.CnpEncrypted)));
    }
}

/// <summary>
/// Adminul cere corecturi: dosarul se redeschide, iar consimțămintele și semnătura se șterg.
/// Datele se vor schimba, deci hash-ul semnat nu mai corespunde — un acord dat pe alt set de
/// date nu are ce căuta în dosar.
/// </summary>
public sealed record RequestCompanyFormationInfoCommand(
    Guid PfaRegistrationId,
    string Reason,
    Guid RequestedByUserId)
    : ICommand<AdminCompanyFormationResponse>;

internal sealed class RequestCompanyFormationInfoCommandHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector)
    : ICommandHandler<RequestCompanyFormationInfoCommand, AdminCompanyFormationResponse>
{
    public async Task<Result<AdminCompanyFormationResponse>> Handle(
        RequestCompanyFormationInfoCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<AdminCompanyFormationResponse>(CompanyFormationErrors.ReasonRequired);
        }

        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .Include(r => r.Owners)
            .Include(r => r.Consents)
            .Include(r => r.Signature)
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == command.PfaRegistrationId, cancellationToken);

        if (request is null)
        {
            return Result.Failure<AdminCompanyFormationResponse>(CompanyFormationErrors.NoRegistration);
        }

        context.CompanyFormationConsents.RemoveRange(request.Consents);
        request.Consents.Clear();

        if (request.Signature is not null)
        {
            context.CompanyFormationSignatures.Remove(request.Signature);
            request.Signature = null;
        }

        request.Status = CompanyFormationStatus.InfoRequested;
        request.CurrentStage = CompanyFormationStage.PersonalData;
        request.AdminNote = command.Reason.Trim();
        request.SubmittedAtUtc = null;
        request.UpdatedAtUtc = DateTime.UtcNow;

        context.PfaActivityLogs.Add(new Domain.PfaRegistrations.PfaActivityLog
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = command.PfaRegistrationId,
            ActivityType = "CompanyFormation.InfoRequested",
            Description = $"A cerut corecturi la dosarul de înființare: {request.AdminNote}",
            PerformedByUserId = command.RequestedByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new AdminCompanyFormationResponse(
            CompanyFormationMapper.ToResponse(request, secretProtector, revealCnp: false),
            [],
            null));
    }
}

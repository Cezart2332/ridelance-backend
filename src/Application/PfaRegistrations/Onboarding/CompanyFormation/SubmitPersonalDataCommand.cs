using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Documents.ExtractedFields;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>
/// Etapa 1 — datele solicitantului. Salvare de draft: se apelează la fiecare ieșire dintr-un
/// câmp, deci acceptă și date incomplete. Validările care blochează avansarea (CNP valid, act
/// neexpirat) se aplică doar peste valorile efectiv trimise.
/// </summary>
public sealed record SubmitPersonalDataCommand(Guid UserId, PersoanaFizicaPayload Payload)
    : ICommand<CompanyFormationResponse>;

internal sealed class SubmitPersonalDataCommandHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector)
    : ICommandHandler<SubmitPersonalDataCommand, CompanyFormationResponse>
{
    /// <summary>Actul trebuie să fie valabil destul cât să se poată depune dosarul.</summary>
    private const int MinDaysBeforeExpiry = 30;

    public async Task<Result<CompanyFormationResponse>> Handle(
        SubmitPersonalDataCommand command,
        CancellationToken cancellationToken)
    {
        Result<CompanyFormationRequest> loaded = await CompanyFormationLoader.ForUserAsync(
            context, command.UserId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CompanyFormationResponse>(loaded.Error);
        }

        CompanyFormationRequest request = loaded.Value;

        string? cnp = command.Payload.Cnp is null
            ? null
            : new string(command.Payload.Cnp.Where(char.IsAsciiDigit).ToArray());

        // Un CNP incomplet e normal în timpul tastării; unul complet dar greșit, nu.
        if (!string.IsNullOrEmpty(cnp) && cnp.Length == 13 && !CnpValidator.IsValid(cnp))
        {
            return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.InvalidCnp);
        }

        if (command.Payload.DataExpirarii is DateOnly expires
            && expires < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(MinDaysBeforeExpiry))
        {
            return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.ExpiredIdentityCard);
        }

        var prefilled = PrefilledFieldMap.Parse(request.PrefilledFields);
        CompanyFormationMapper.Apply(request.Solicitant, command.Payload, secretProtector, prefilled);

        request.PrefilledFields = prefilled.Serialize();
        request.UpdatedAtUtc = DateTime.UtcNow;

        if (request.PersonalDataComplete && request.CurrentStage == CompanyFormationStage.PersonalData)
        {
            request.CurrentStage = CompanyFormationStage.RegisteredOffice;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(
            CompanyFormationMapper.ToResponse(request, secretProtector, revealCnp: true));
    }
}

/// <summary>
/// Încărcarea dosarului editabil al userului curent, cu toate verificările comune: există un
/// dosar PFA, e pe ramura „Nu am PFA" și nu a fost deja semnat. Toate cele trei comenzi de
/// editare au nevoie de exact aceiași pași.
/// </summary>
internal static class CompanyFormationLoader
{
    public static async Task<Result<CompanyFormationRequest>> ForUserAsync(
        IApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<CompanyFormationRequest>(CompanyFormationErrors.NoRegistration);
        }

        if (registration.RegistrationType != RegistrationType.NuAmPfa)
        {
            return Result.Failure<CompanyFormationRequest>(CompanyFormationErrors.WrongBranch);
        }

        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .Include(r => r.Owners)
            .Include(r => r.Consents)
            .Include(r => r.Signature)
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == registration.Id, cancellationToken);

        if (request is null)
        {
            request = new CompanyFormationRequest
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = registration.Id,
            };
            context.CompanyFormationRequests.Add(request);
        }
        else if (request.IsLocked)
        {
            return Result.Failure<CompanyFormationRequest>(CompanyFormationErrors.Locked);
        }

        return Result.Success(request);
    }
}

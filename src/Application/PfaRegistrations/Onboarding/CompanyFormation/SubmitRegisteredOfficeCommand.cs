using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Documents.ExtractedFields;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>
/// Etapa 2 — sediul social. Ca și etapa 1, e o salvare de draft: acceptă date incomplete,
/// dar refuză date imposibile (adresă Consulto inexistentă, CNP de proprietar greșit).
/// </summary>
public sealed record SubmitRegisteredOfficeCommand(Guid UserId, RegisteredOfficePayload Payload)
    : ICommand<CompanyFormationResponse>;

internal sealed class SubmitRegisteredOfficeCommandHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector)
    : ICommandHandler<SubmitRegisteredOfficeCommand, CompanyFormationResponse>
{
    public async Task<Result<CompanyFormationResponse>> Handle(
        SubmitRegisteredOfficeCommand command,
        CancellationToken cancellationToken)
    {
        Result<CompanyFormationRequest> loaded = await CompanyFormationLoader.ForUserAsync(
            context, command.UserId, cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<CompanyFormationResponse>(loaded.Error);
        }

        CompanyFormationRequest request = loaded.Value;

        // Gating pe server, nu doar în UI: nu se trece la sediu fără datele solicitantului.
        if (!request.PersonalDataComplete)
        {
            return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.PersonalDataIncomplete);
        }

        RegisteredOfficePayload payload = command.Payload;

        RegisteredOfficeType? officeType = Enum.TryParse(payload.Type, ignoreCase: true, out RegisteredOfficeType parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : null;

        if (officeType == RegisteredOfficeType.ConsultoProvided && payload.ConsultoOfficeId is Guid officeId)
        {
            bool exists = await context.ConsultoOffices
                .AnyAsync(o => o.Id == officeId && o.IsActive, cancellationToken);

            if (!exists)
            {
                return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.ConsultoOfficeNotFound);
            }
        }

        IReadOnlyList<OwnerPayload> owners = payload.Owners ?? [];

        foreach (OwnerPayload owner in owners)
        {
            string? cnp = Digits(owner.Persoana.Cnp);
            if (cnp?.Length == 13 && !CnpValidator.IsValid(cnp))
            {
                return Result.Failure<CompanyFormationResponse>(CompanyFormationErrors.InvalidCnp);
            }
        }

        request.OfficeType = officeType;
        request.ConsultoOfficeId = officeType == RegisteredOfficeType.ConsultoProvided
            ? payload.ConsultoOfficeId
            : null;
        request.IsOwner = officeType == RegisteredOfficeType.Own ? payload.IsOwner : null;

        // Marcajul de „editat manual" e util doar pe datele venite din OCR; sediul se
        // completează integral de mână, deci harta primește o instanță de unică folosință.
        CompanyFormationMapper.ApplyAddress(request.OfficeAddress, payload.Adresa, "SEDIU", PrefilledFieldMap.Untracked());

        request.AcknowledgedOwnershipDocs = payload.AcknowledgedOwnershipDocs;
        request.AcknowledgedSubmitLater = payload.AcknowledgedSubmitLater;
        request.AcknowledgedOwnerConsent = payload.AcknowledgedOwnerConsent;

        ReconcileOwners(request, owners);

        request.UpdatedAtUtc = DateTime.UtcNow;

        if (request.RegisteredOfficeComplete && request.CurrentStage == CompanyFormationStage.RegisteredOffice)
        {
            request.CurrentStage = CompanyFormationStage.Consent;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(
            CompanyFormationMapper.ToResponse(request, secretProtector, revealCnp: true));
    }

    /// <summary>
    /// Aliniază lista din DB la cea trimisă: cei fără id sunt noi, cei absenți din payload au
    /// fost eliminați în pagină. Proprietarii rămân salvați și când ramura curentă nu-i cere,
    /// ca o bifă schimbată din greșeală să nu șteargă datele deja tastate.
    /// </summary>
    private void ReconcileOwners(CompanyFormationRequest request, IReadOnlyList<OwnerPayload> payload)
    {
        HashSet<Guid> keep = [.. payload.Where(o => o.Id is not null).Select(o => o.Id!.Value)];

        foreach (CompanyFormationOwner removed in request.Owners.Where(o => !keep.Contains(o.Id)).ToList())
        {
            request.Owners.Remove(removed);
            context.CompanyFormationOwners.Remove(removed);
        }

        for (int position = 0; position < payload.Count; position++)
        {
            OwnerPayload incoming = payload[position];

            CompanyFormationOwner? owner = incoming.Id is Guid id
                ? request.Owners.FirstOrDefault(o => o.Id == id)
                : null;

            if (owner is null)
            {
                // Id-ul vine din pagină: două autosave-uri pornite înainte ca primul să
                // răspundă trebuie să scrie în același rând, nu să creeze doi proprietari.
                owner = new CompanyFormationOwner
                {
                    Id = incoming.Id ?? Guid.NewGuid(),
                    CompanyFormationRequestId = request.Id,
                };

                // Prin DbSet, nu doar prin colecția de navigație: cu Id-ul deja setat, EF ar
                // marca entitatea Modified și ar emite UPDATE pe un rând care nu există.
                context.CompanyFormationOwners.Add(owner);
                request.Owners.Add(owner);
            }

            owner.Position = position;
            owner.UpdatedAtUtc = DateTime.UtcNow;

            // Proprietarii nu au CI încărcată, deci nimic nu vine din OCR pentru ei.
            CompanyFormationMapper.Apply(owner.Persoana, incoming.Persoana, secretProtector, PrefilledFieldMap.Untracked());
        }
    }

    private static string? Digits(string? value) =>
        value is null ? null : new string(value.Where(char.IsAsciiDigit).ToArray());
}

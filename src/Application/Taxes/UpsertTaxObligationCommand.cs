using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Documents.AiVerification;
using Domain.PfaRegistrations;
using Domain.Taxes;
using Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Taxes;

/// <param name="Id">Null la creare. Cu valoare, se actualizează obligația existentă.</param>
public sealed record UpsertTaxObligationCommand(
    Guid? Id,
    Guid PfaRegistrationId,
    string Type,
    int PeriodYear,
    int PeriodMonth,
    decimal AmountDue,
    DateOnly DueDate,
    string Status,
    Guid? DocumentId,
    string? Note) : ICommand<TaxObligationResponse>;

internal sealed class UpsertTaxObligationCommandValidator : AbstractValidator<UpsertTaxObligationCommand>
{
    public UpsertTaxObligationCommandValidator()
    {
        RuleFor(c => c.PfaRegistrationId).NotEmpty();
        RuleFor(c => c.PeriodYear).InclusiveBetween(2000, 2100);
        RuleFor(c => c.PeriodMonth).InclusiveBetween(1, 12);
        RuleFor(c => c.AmountDue).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Note).MaximumLength(1000);

        RuleFor(c => c.Type)
            .Must(value => Enum.TryParse<TaxObligationType>(value, ignoreCase: true, out _))
            .WithMessage("Tipul obligației nu este valid.");

        RuleFor(c => c.Status)
            .Must(value => Enum.TryParse<TaxObligationStatus>(value, ignoreCase: true, out _))
            .WithMessage("Statusul obligației nu este valid.");
    }
}

/// <summary>
/// Creează sau actualizează o obligație fiscală. Numai contabila asignată și adminul pot —
/// clientul o citește, dar nu o scrie: dacă și-ar putea edita singur suma de plată, distincția
/// dintre estimare și obligație s-ar șterge exact acolo unde contează.
/// </summary>
internal sealed class UpsertTaxObligationCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpsertTaxObligationCommand, TaxObligationResponse>
{
    public async Task<Result<TaxObligationResponse>> Handle(
        UpsertTaxObligationCommand command,
        CancellationToken cancellationToken)
    {
        Guid userId = userContext.UserId;

        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        PfaRegistration? pfa = await context.PfaRegistrations
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<TaxObligationResponse>(
                Error.NotFound("PfaRegistration.NotFound", "PFA-ul nu a fost găsit."));
        }

        bool canManage = caller is not null &&
            (caller.Role is UserRole.Admin
                || caller.Role is UserRole.Contabil && pfa.AssignedContabilId == userId);

        if (!canManage)
        {
            return Result.Failure<TaxObligationResponse>(
                Error.Failure("TaxObligation.Forbidden", "Doar contabila asignată poate gestiona declarațiile."));
        }

        TaxObligationType type = Enum.Parse<TaxObligationType>(command.Type, ignoreCase: true);
        TaxObligationStatus status = Enum.Parse<TaxObligationStatus>(command.Status, ignoreCase: true);

        TaxObligation? obligation = command.Id is null
            ? null
            : await context.TaxObligations
                .SingleOrDefaultAsync(
                    o => o.Id == command.Id && o.PfaRegistrationId == command.PfaRegistrationId,
                    cancellationToken);

        if (command.Id is not null && obligation is null)
        {
            return Result.Failure<TaxObligationResponse>(
                Error.NotFound("TaxObligation.NotFound", "Obligația nu a fost găsită."));
        }

        if (obligation is null)
        {
            obligation = new TaxObligation
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = command.PfaRegistrationId,
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.TaxObligations.Add(obligation);
        }

        obligation.Type = type;
        obligation.PeriodYear = command.PeriodYear;
        obligation.PeriodMonth = command.PeriodMonth;
        obligation.AmountDue = command.AmountDue;
        obligation.DueDate = command.DueDate;
        obligation.Status = status;
        obligation.DocumentId = command.DocumentId;
        obligation.Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim();
        obligation.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return TaxObligationMapper.ToResponse(obligation, DocumentDateValidator.TodayInRomania());
    }
}

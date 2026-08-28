using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Rentals;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Rentals.Payments;

public sealed record RentalPaymentDto(
    Guid Id,
    long AmountBani,
    DateTime PaidOnUtc,
    string Method,
    string? Notes);

/// <param name="ContractValueBani">Valoarea perioadei, din chirie și durată.</param>
/// <param name="RecordedBani">Cât s-a înregistrat ca încasat.</param>
/// <param name="RemainingBani">Diferența. Poate fi negativă: s-a încasat mai mult decât contractul.</param>
public sealed record RentalPaymentsDto(
    long ContractValueBani,
    long RecordedBani,
    long RemainingBani,
    IReadOnlyList<RentalPaymentDto> Payments);

public sealed record GetRentalPaymentsQuery(Guid RentalId) : IQuery<RentalPaymentsDto>;

public sealed record AddRentalPaymentCommand(
    Guid RentalId,
    long AmountBani,
    DateTime PaidOnUtc,
    string Method,
    string? Notes) : ICommand<Guid>;

public sealed record DeleteRentalPaymentCommand(Guid PaymentId) : ICommand;

internal sealed class GetRentalPaymentsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : IQueryHandler<GetRentalPaymentsQuery, RentalPaymentsDto>
{
    public async Task<Result<RentalPaymentsDto>> Handle(
        GetRentalPaymentsQuery query,
        CancellationToken cancellationToken)
    {
        Rental? rental = await context.Rentals
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.Id == query.RentalId && r.OwnerUserId == userContext.UserId,
                cancellationToken);

        if (rental is null)
        {
            return Result.Failure<RentalPaymentsDto>(
                Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        List<RentalPayment> payments = await context.RentalPayments
            .AsNoTracking()
            .Where(p => p.RentalId == query.RentalId)
            .OrderByDescending(p => p.PaidOnUtc)
            .ToListAsync(cancellationToken);

        long contract = RentalContractValue.For(rental);
        long recorded = payments.Sum(p => p.AmountBani);

        return Result.Success(new RentalPaymentsDto(
            contract,
            recorded,
            contract - recorded,
            payments
                .Select(p => new RentalPaymentDto(p.Id, p.AmountBani, p.PaidOnUtc, p.Method.ToString(), p.Notes))
                .ToList()));
    }
}

internal sealed class AddRentalPaymentCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<AddRentalPaymentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddRentalPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AmountBani <= 0)
        {
            return Result.Failure<Guid>(Error.Problem("Payment.Invalid", "Suma trebuie să fie mai mare decât zero."));
        }

        bool owns = await context.Rentals
            .AsNoTracking()
            .AnyAsync(r => r.Id == command.RentalId && r.OwnerUserId == userContext.UserId, cancellationToken);

        if (!owns)
        {
            return Result.Failure<Guid>(Error.NotFound("Rental.NotFound", "Închirierea nu a fost găsită."));
        }

        if (!Enum.TryParse(command.Method, ignoreCase: true, out RentalPaymentMethod method))
        {
            method = RentalPaymentMethod.Other;
        }

        var payment = new RentalPayment
        {
            Id = Guid.NewGuid(),
            RentalId = command.RentalId,
            AmountBani = command.AmountBani,
            PaidOnUtc = command.PaidOnUtc,
            Method = method,
            Notes = command.Notes?.Trim(),
        };

        context.RentalPayments.Add(payment);
        await context.SaveChangesAsync(cancellationToken);

        return payment.Id;
    }
}

internal sealed class DeleteRentalPaymentCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<DeleteRentalPaymentCommand>
{
    public async Task<Result> Handle(DeleteRentalPaymentCommand command, CancellationToken cancellationToken)
    {
        RentalPayment? payment = await context.RentalPayments
            .Include(p => p.Rental)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, cancellationToken);

        if (payment is null || payment.Rental.OwnerUserId != userContext.UserId)
        {
            return Result.Failure(Error.NotFound("Payment.NotFound", "Plata nu a fost găsită."));
        }

        context.RentalPayments.Remove(payment);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Valoarea contractuală a unei închirieri: chiria săptămânală înmulțită cu durata.
/// </summary>
/// <remarks>
/// Aceeași formulă ca în lista de închirieri, scoasă aici ca să nu existe două. Două calcule ale
/// aceleiași sume ajung, în timp, să difere — iar utilizatorul vede două cifre pentru același
/// contract, în două ecrane.
/// </remarks>
internal static class RentalContractValue
{
    private const decimal DaysPerWeek = 7m;

    public static long For(Rental rental)
    {
        decimal weeks = (decimal)(rental.EndAtUtc - rental.StartAtUtc).TotalDays / DaysPerWeek;
        return (long)Math.Round(rental.WeeklyRentBani * Math.Max(weeks, 0m));
    }
}

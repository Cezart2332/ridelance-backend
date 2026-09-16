using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Banking;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

/// <summary>Un cont legat prin open banking, cum îl vede adminul.</summary>
public sealed record AdminBankAccountSummary(string? IbanMasked, string? Currency, string? OwnerName);

/// <summary>Conexiunea bancară a clientului, pe scurt.</summary>
public sealed record AdminBankSummary(
    string Status,
    string? InstitutionName,
    DateTime? LinkedAtUtc,
    IReadOnlyList<AdminBankAccountSummary> Accounts);

/// <summary>Tot ce a făcut clientul la pasul 3, pentru verificarea din admin.</summary>
public sealed record AdminFiscalReviewResponse(Step2StateResponse Step2, AdminBankSummary? Bank);

/// <summary>
/// Pasul 3 văzut din admin: răspunsul la TVA, banca legată și contul Oblio.
///
/// Există fiindcă pasul ăsta aproape n-are documente. TVA-ul „Nu" nu produce niciun act, banca vine
/// prin open banking, iar pachetul de semnături circulă pe email — deci în admin pasul arăta
/// „niciun document încărcat", deși clientul făcuse tot. Iar de când fiecare pas se deschide doar pe
/// validarea adminului, adminul are nevoie să vadă ce validează.
/// </summary>
public sealed record GetAdminFiscalReviewQuery(Guid RegistrationId) : IQuery<AdminFiscalReviewResponse>;

internal sealed class GetAdminFiscalReviewQueryHandler(
    IApplicationDbContext context,
    IQueryHandler<GetStep2StateQuery, Step2StateResponse> step2)
    : IQueryHandler<GetAdminFiscalReviewQuery, AdminFiscalReviewResponse>
{
    public async Task<Result<AdminFiscalReviewResponse>> Handle(
        GetAdminFiscalReviewQuery query,
        CancellationToken cancellationToken)
    {
        Guid? userId = await context.PfaRegistrations
            .AsNoTracking()
            .Where(r => r.Id == query.RegistrationId)
            .Select(r => (Guid?)r.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is not Guid owner)
        {
            return Result.Failure<AdminFiscalReviewResponse>(PfaRegistrationErrors.NotFound(query.RegistrationId));
        }

        // Aceeași sursă ca ecranul clientului: adminul și clientul văd aceleași răspunsuri.
        Result<Step2StateResponse> state = await step2.Handle(new GetStep2StateQuery(owner), cancellationToken);
        if (state.IsFailure)
        {
            return Result.Failure<AdminFiscalReviewResponse>(state.Error);
        }

        BankConnection? connection = await context.BankConnections
            .AsNoTracking()
            .Include(c => c.Accounts)
            .FirstOrDefaultAsync(c => c.UserId == owner, cancellationToken);

        AdminBankSummary? bank = connection is null
            ? null
            : new AdminBankSummary(
                connection.Status.ToString(),
                connection.InstitutionName,
                connection.LinkedAtUtc,
                [.. connection.Accounts
                    .Where(a => a.IsActive)
                    .Select(a => new AdminBankAccountSummary(a.IbanMasked, a.Currency, a.OwnerName))]);

        return new AdminFiscalReviewResponse(state.Value, bank);
    }
}

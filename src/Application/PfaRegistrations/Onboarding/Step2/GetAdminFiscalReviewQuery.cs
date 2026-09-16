using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Application.Documents.ExtractedFields;
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
/// <param name="DeclaredIban">
/// IBAN-ul declarat, întreg — nu mascat ca pe ecranul clientului. Adminul îl verifică pe extras
/// sau în bancă, iar cu mască nu avea ce compara. Conturile legate prin open banking vin doar
/// mascate de la furnizor, deci pentru ele nu există o valoare întreagă de arătat.
/// </param>
public sealed record AdminFiscalReviewResponse(
    Step2StateResponse Step2,
    AdminBankSummary? Bank,
    string? DeclaredIban);

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
    IQueryHandler<GetStep2StateQuery, Step2StateResponse> step2,
    ISecretProtector secretProtector)
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

        string? encryptedIban = await context.PfaRegistrations
            .AsNoTracking()
            .Where(r => r.Id == query.RegistrationId)
            .Select(r => r.BankAccountDeclaration!.IbanEncrypted)
            .FirstOrDefaultAsync(cancellationToken);

        string? declaredIban = SensitiveFieldProtection.TryUnprotect(secretProtector, encryptedIban);

        return new AdminFiscalReviewResponse(state.Value, bank, declaredIban);
    }
}

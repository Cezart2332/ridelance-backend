using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Security;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal sealed class SubmitBankDeclarationCommandHandler(
    IApplicationDbContext context,
    ISecretProtector secretProtector)
    : ICommandHandler<SubmitBankDeclarationCommand, Step2BankDto>
{
    private static readonly Error InvalidIban = Error.Failure(
        "Onboarding.Step2.InvalidIban",
        "IBAN-ul introdus nu este valid.");

    public async Task<Result<Step2BankDto>> Handle(
        SubmitBankDeclarationCommand command,
        CancellationToken cancellationToken)
    {
        // Document-first: userul alege doar banca; IBAN-ul se citește din documentul încărcat (OCR).
        // Dacă totuși e trimis explicit, trebuie să fie plauzibil.
        string iban = NormalizeIban(command.Iban ?? string.Empty);
        bool hasIban = iban.Length > 0;
        if (hasIban && !IsPlausibleIban(iban))
        {
            return Result.Failure<Step2BankDto>(InvalidIban);
        }

        PfaRegistration? registration = await context.PfaRegistrations
            .Include(r => r.BankAccountDeclaration)
            .Where(r => r.UserId == command.UserId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<Step2BankDto>(Step2Errors.NoRegistration);
        }

        string? masked = hasIban ? MaskIban(iban) : null;
        DateTime nowUtc = DateTime.UtcNow;

        // Dacă userul are deja o conexiune PSD2 activă cu același IBAN, validăm automat.
        var linkedAccount = masked is null ? null : await context.BankAccounts
            .AsNoTracking()
            .Where(a => a.UserId == command.UserId && a.IsActive && a.IbanMasked == masked)
            .Select(a => new { a.Id, a.BankConnectionId, a.OwnerName })
            .FirstOrDefaultAsync(cancellationToken);

        PfaBankAccountDeclaration declaration = registration.BankAccountDeclaration ?? new PfaBankAccountDeclaration
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = registration.Id,
            CreatedAtUtc = nowUtc,
        };

        if (registration.BankAccountDeclaration is null)
        {
            context.PfaBankAccountDeclarations.Add(declaration);
        }

        declaration.BankName = command.BankName;
        declaration.ConfirmationDocumentId = command.ConfirmationDocumentId ?? declaration.ConfirmationDocumentId;
        declaration.UpdatedAtUtc = nowUtc;

        // IBAN-ul citit din document (OCR) nu se șterge dacă userul salvează doar banca.
        if (hasIban)
        {
            declaration.IbanEncrypted = secretProtector.Protect(iban);
            declaration.IbanMasked = masked;
        }

        if (linkedAccount is not null)
        {
            declaration.Source = BankDeclarationSource.OpenBanking;
            declaration.Status = BankDeclarationStatus.Verified;
            declaration.BankConnectionId = linkedAccount.BankConnectionId;
        }
        else if (declaration.Status != BankDeclarationStatus.Verified)
        {
            declaration.Source = BankDeclarationSource.Manual;
            declaration.Status = BankDeclarationStatus.Pending;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new Step2BankDto(
            declaration.BankName,
            declaration.IbanMasked,
            declaration.ConfirmationDocumentId is not null,
            declaration.OcrIbanMatches,
            declaration.Source.ToString(),
            declaration.Status.ToString()));
    }

    private static string NormalizeIban(string raw) =>
        new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    // Verificare de plauzibilitate (nu checksum complet): RO + 2 cifre + 20 alfanumerice.
    private static bool IsPlausibleIban(string iban) =>
        iban.Length is >= 15 and <= 34 &&
        iban.Length >= 2 && char.IsLetter(iban[0]) && char.IsLetter(iban[1]) &&
        iban.Skip(2).All(char.IsLetterOrDigit);

    private static string MaskIban(string iban) =>
        iban.Length <= 8 ? iban : $"{iban[..4]}••••{iban[^4..]}";
}

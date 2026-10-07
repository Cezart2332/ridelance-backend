using FluentValidation;

namespace Application.PfaRegistrations.Create;

internal sealed class CreatePfaRegistrationCommandValidator
    : AbstractValidator<CreatePfaRegistrationCommand>
{
    public CreatePfaRegistrationCommandValidator()
    {
        // „Am PFA" nu mai cere nimic la creare. Telefonul îl ia handlerul din cont (numărul dat la
        // înregistrare) — ecranul separat de „Date de contact” a dispărut din înrolare. Nici numele,
        // nici CUI-ul nu se tastează: userul încarcă buletinul și certificatul de înregistrare, iar
        // OCR-ul completează `User.FirstName/LastName` (`ExtractedFieldApplier.ApplyToUserAsync`) și
        // `Cui`. Validarea de checksum a CUI-ului rămâne la aprobarea adminului.
        //
        // „Nu am PFA" nu mai cere nimic la creare: adresa sediului, proprietarul și restul
        // datelor se colectează în dosarul de înființare (CompanyFormationRequest), care e
        // sursa de adevăr. Câmpurile de adresă de pe PfaRegistration rămân doar pentru
        // dosarele create înainte de fluxul nou.
        RuleFor(x => x.RegistrationType).IsInEnum();
    }
}

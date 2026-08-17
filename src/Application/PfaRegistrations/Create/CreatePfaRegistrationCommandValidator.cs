using Domain.PfaRegistrations;
using FluentValidation;

namespace Application.PfaRegistrations.Create;

internal sealed class CreatePfaRegistrationCommandValidator
    : AbstractValidator<CreatePfaRegistrationCommand>
{
    public CreatePfaRegistrationCommandValidator()
    {
        When(x => x.RegistrationType == RegistrationType.AmPfa, () =>
        {
            RuleFor(x => x.Phone).NotEmpty().WithMessage("Phone number is required.");
            // Nici numele, nici CUI-ul nu se mai tastează: userul încarcă buletinul și
            // certificatul de înregistrare, iar OCR-ul completează `User.FirstName/LastName`
            // (`ExtractedFieldApplier.ApplyToUserAsync`) și `Cui`. Cerute și aici, ar fi a doua
            // sursă pentru aceeași informație. Validarea de checksum a CUI-ului rămâne la
            // aprobarea adminului.
        });

        // „Nu am PFA" nu mai cere nimic la creare: adresa sediului, proprietarul și restul
        // datelor se colectează în dosarul de înființare (CompanyFormationRequest), care e
        // sursa de adevăr. Câmpurile de adresă de pe PfaRegistration rămân doar pentru
        // dosarele create înainte de fluxul nou.
    }
}

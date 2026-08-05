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
            RuleFor(x => x.FullName).NotEmpty().WithMessage("Full name is required.");
            RuleFor(x => x.Phone).NotEmpty().WithMessage("Phone number is required.");
            // CUI-ul nu se mai tastează: userul încarcă certificatul de înregistrare, iar OCR-ul
            // completează `Cui`. Validarea de checksum rămâne la aprobarea adminului.
        });

        // „Nu am PFA" nu mai cere nimic la creare: adresa sediului, proprietarul și restul
        // datelor se colectează în dosarul de înființare (CompanyFormationRequest), care e
        // sursa de adevăr. Câmpurile de adresă de pe PfaRegistration rămân doar pentru
        // dosarele create înainte de fluxul nou.
    }
}

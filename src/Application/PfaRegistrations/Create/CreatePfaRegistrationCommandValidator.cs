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
        });

        When(x => x.RegistrationType == RegistrationType.NuAmPfa, () =>
        {
            RuleFor(x => x.ContractDuration).NotNull().GreaterThan(0).WithMessage("Contract duration must be specified.");
            RuleFor(x => x.Street).NotEmpty().WithMessage("Street is required.");
            RuleFor(x => x.Number).NotEmpty().WithMessage("Number is required.");
            RuleFor(x => x.City).NotEmpty().WithMessage("City is required.");
            RuleFor(x => x.County).NotEmpty().WithMessage("County is required.");
        });
    }
}

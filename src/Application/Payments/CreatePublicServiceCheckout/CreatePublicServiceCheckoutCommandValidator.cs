using FluentValidation;

namespace Application.Payments.CreatePublicServiceCheckout;

internal sealed class CreatePublicServiceCheckoutCommandValidator
    : AbstractValidator<CreatePublicServiceCheckoutCommand>
{
    private static readonly string[] AllowedKeys =
    [
        "infiintare_pfa",
        "sediu_social",
        "start_ride",
    ];

    public CreatePublicServiceCheckoutCommandValidator()
    {
        RuleFor(c => c.ServiceKey)
            .NotEmpty()
            .Must(k => AllowedKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Serviciul selectat nu este valid.");

        RuleFor(c => c.CustomerName)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(c => c.CustomerEmail)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(c => c.CustomerPhone)
            .NotEmpty()
            .MaximumLength(32);
    }
}

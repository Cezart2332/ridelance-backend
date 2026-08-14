using FluentValidation;

namespace Application.Users.Register;

internal sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        // RL-05: numele NU se mai cere la înregistrare — vine din buletin, la pasul de
        // eligibilitate. Coloanele rămân, doar cerința a dispărut.
        RuleFor(c => c.Email).NotEmpty().EmailAddress();
        RuleFor(c => c.Password).NotEmpty().MinimumLength(8);
    }
}

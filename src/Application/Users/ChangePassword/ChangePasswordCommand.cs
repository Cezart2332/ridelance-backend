using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : ICommand;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(c => c.CurrentPassword).NotEmpty();
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(8)
            .WithMessage("Parola nouă trebuie să aibă cel puțin 8 caractere.");
        RuleFor(c => c.NewPassword).NotEqual(c => c.CurrentPassword)
            .WithMessage("Parola nouă trebuie să fie diferită de cea curentă.");
    }
}

/// <summary>
/// Schimbarea parolei din Profil. Se cere parola curentă chiar dacă utilizatorul e deja
/// autentificat: o sesiune lăsată deschisă pe un dispozitiv străin nu trebuie să fie suficientă
/// ca să preiei contul.
/// </summary>
internal sealed class ChangePasswordCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IPasswordHasher passwordHasher)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Result> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        if (!passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure(
                Error.Problem("Users.WrongPassword", "Parola curentă nu este corectă."));
        }

        user.PasswordHash = passwordHasher.Hash(command.NewPassword);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

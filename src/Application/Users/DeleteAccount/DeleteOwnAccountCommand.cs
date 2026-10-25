using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.Admin.Accounts;
using Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.DeleteAccount;

public sealed record DeleteOwnAccountCommand(string Password) : ICommand;

internal sealed class DeleteOwnAccountCommandValidator : AbstractValidator<DeleteOwnAccountCommand>
{
    public DeleteOwnAccountCommandValidator()
    {
        RuleFor(c => c.Password).NotEmpty();
    }
}

/// <summary>
/// Ștergerea contului din Profil, cerută de client. Se face ca închiderea din admin: accesul se
/// pierde și abonamentele se opresc, dar dosarul, documentele și plățile rămân, pentru obligațiile
/// contabile și legale. Se cere parola, ca o sesiune lăsată deschisă să nu fie de ajuns.
/// </summary>
internal sealed class DeleteOwnAccountCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IPasswordHasher passwordHasher,
    IStripeService stripe)
    : ICommandHandler<DeleteOwnAccountCommand>
{
    private static readonly Error StripeCancelFailed = Error.Problem(
        "Users.DeleteAccountBillingFailed",
        "Nu am putut opri abonamentul, deci contul nu a fost șters. Încearcă din nou peste câteva minute.");

    public async Task<Result> Handle(DeleteOwnAccountCommand command, CancellationToken cancellationToken)
    {
        User? user = await context.Users
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (user is null || user.IsDeleted)
        {
            return Result.Failure(Error.Failure("Auth.Unauthorized", "Utilizator neautentificat."));
        }

        // Echipa nu își închide singură contul: îl închide un admin, ca să nu rămână platforma fără.
        if (user.Role is not (UserRole.Client or UserRole.CarPoster))
        {
            return Result.Failure(UserErrors.CannotCloseStaffAccount);
        }

        if (!passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return Result.Failure(Error.Problem("Users.WrongPassword", "Parola nu este corectă."));
        }

        Result closed = await ClientAccounts.CloseAsync(
            context, stripe, user, user.Id, "Șters de client din aplicație", cancellationToken);
        if (closed.IsFailure)
        {
            return Result.Failure(StripeCancelFailed);
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

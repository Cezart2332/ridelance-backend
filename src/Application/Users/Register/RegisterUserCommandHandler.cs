using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Users.Register;

internal sealed class RegisterUserCommandHandler(IApplicationDbContext context, IPasswordHasher passwordHasher)
    : ICommandHandler<RegisterUserCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (await context.Users.AnyAsync(u => u.Email == command.Email, cancellationToken))
        {
            return Result.Failure<Guid>(UserErrors.EmailNotUnique);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = command.Email,
            FirstName = command.FirstName?.Trim() ?? string.Empty,
            LastName = command.LastName?.Trim() ?? string.Empty,
            PasswordHash = passwordHasher.Hash(command.Password),
            Role = command.Role,
            PhoneNumber = command.PhoneNumber,
            CreatedAtUtc = DateTime.UtcNow
        };

        // Codul se scrie odată cu contul, într-o singură salvare. Emailul pleacă din
        // `UserRegisteredDomainEventHandler`, care rulează după `SaveChangesAsync`, deci codul e
        // deja în baza de date când ajunge mesajul — nu invers.
        EmailVerificationCodes.Issue(user);

        user.Raise(new UserRegisteredDomainEvent(user.Id));

        context.Users.Add(user);

        await context.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}

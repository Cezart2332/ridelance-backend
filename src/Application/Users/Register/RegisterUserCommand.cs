using Application.Abstractions.Messaging;
using Domain.Users;

namespace Application.Users.Register;

public sealed record RegisterUserCommand(
    string Email,
    string FirstName,
    string LastName,
    string Password,
    UserRole Role = UserRole.Client,
    string? PhoneNumber = null) : ICommand<Guid>;

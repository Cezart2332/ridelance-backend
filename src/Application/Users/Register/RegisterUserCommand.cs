using Application.Abstractions.Messaging;
using Domain.Users;

namespace Application.Users.Register;

/// <summary>
/// RL-05 — numele e opțional la înregistrare. Se completează automat din buletin (RL-04) sau
/// manual de admin. Coloanele din DB rămân NOT NULL, cu șir gol.
/// </summary>
public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string? FirstName = null,
    string? LastName = null,
    UserRole Role = UserRole.Client,
    string? PhoneNumber = null) : ICommand<Guid>;

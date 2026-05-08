using Application.Abstractions.Messaging;

namespace Application.Users.GetProfile;

public sealed record GetProfileQuery(Guid UserId) : IQuery<UserProfileResponse>;

public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string Role,
    DateTime CreatedAtUtc);

using Application.Abstractions.Messaging;

namespace Application.Users.GetProfile;

public sealed record GetProfileQuery(Guid UserId) : IQuery<UserProfileResponse>;

public sealed record UserProfileResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    /// <summary>Numărul a fost confirmat prin SMS. Vezi <c>PhoneVerification</c>.</summary>
    bool IsPhoneVerified,
    string Role,
    DateTime CreatedAtUtc);

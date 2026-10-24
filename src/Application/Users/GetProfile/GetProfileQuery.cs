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
    DateTime CreatedAtUtc,
    bool IsEmailVerified = false,
    /// <summary>Confirmări cerute înainte de orice alt pas (vezi <c>AccountVerificationGate</c>).</summary>
    bool PendingEmailVerification = false,
    bool PendingPhoneVerification = false);

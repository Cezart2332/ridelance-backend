using SharedKernel;

namespace Domain.Users;

public static class UserErrors
{
    public static Error NotFound(Guid userId) => Error.NotFound(
        "Users.NotFound",
        $"The user with the Id = '{userId}' was not found");

    public static Error Unauthorized() => Error.Failure(
        "Users.Unauthorized",
        "You are not authorized to perform this action.");

    public static readonly Error NotFoundByEmail = Error.NotFound(
        "Users.NotFoundByEmail",
        "The user with the specified email was not found");

    public static readonly Error EmailNotUnique = Error.Conflict(
        "Users.EmailNotUnique",
        "The provided email is not unique");

    public static readonly Error InvalidCredentials = Error.Failure(
        "Users.InvalidCredentials",
        "The provided email or password is incorrect.");

    public static readonly Error InvalidRefreshToken = Error.Failure(
        "Users.InvalidRefreshToken",
        "The refresh token is invalid or has expired.");

    public static readonly Error InvalidRole = Error.Failure(
        "Users.InvalidRole",
        "The specified role is not valid.");
}

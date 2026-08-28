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

    /// <summary>
    /// Cod greșit — sau adresă fără cont. Deliberat același mesaj pentru ambele: unul distinct ar
    /// fi spus oricui care adrese sunt înregistrate pe platformă.
    /// </summary>
    public static readonly Error VerificationCodeInvalid = Error.Failure(
        "Users.VerificationCodeInvalid",
        "Codul introdus nu este corect.");

    public static readonly Error VerificationCodeExpired = Error.Failure(
        "Users.VerificationCodeExpired",
        "Codul a expirat. Cere unul nou.");

    public static readonly Error VerificationCodeMissing = Error.Failure(
        "Users.VerificationCodeMissing",
        "Nu există un cod activ pentru această adresă. Cere unul nou.");

    public static readonly Error VerificationTooManyAttempts = Error.Failure(
        "Users.VerificationTooManyAttempts",
        "Prea multe încercări. Cere un cod nou.");

    public static readonly Error VerificationResendTooSoon = Error.Failure(
        "Users.VerificationResendTooSoon",
        "Am trimis deja un cod. Așteaptă un minut înainte să ceri altul.");

    public static readonly Error PhoneMissing = Error.Failure(
        "Users.PhoneMissing",
        "Adaugă un număr de telefon înainte de a-l confirma.");

    public static readonly Error PhoneInvalid = Error.Failure(
        "Users.PhoneInvalid",
        "Numărul de telefon nu pare valid. Scrie-l în forma 07XX XXX XXX.");

    public static readonly Error InvalidRefreshToken = Error.Failure(
        "Users.InvalidRefreshToken",
        "The refresh token is invalid or has expired.");

    public static readonly Error InvalidRole = Error.Failure(
        "Users.InvalidRole",
        "The specified role is not valid.");
}

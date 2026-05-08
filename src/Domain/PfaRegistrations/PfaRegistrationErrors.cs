using SharedKernel;

namespace Domain.PfaRegistrations;

public static class PfaRegistrationErrors
{
    public static Error NotFound(Guid registrationId) => Error.NotFound(
        "PfaRegistrations.NotFound",
        $"The PFA registration with Id = '{registrationId}' was not found.");

    public static readonly Error AlreadyExists = Error.Conflict(
        "PfaRegistrations.AlreadyExists",
        "A PFA registration already exists for this user.");

    public static readonly Error AlreadyReviewed = Error.Problem(
        "PfaRegistrations.AlreadyReviewed",
        "This PFA registration has already been reviewed.");
}

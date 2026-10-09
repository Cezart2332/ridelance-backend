using SharedKernel;

namespace Domain.PfaRegistrations.ArrFleet;

public static class ArrFleetErrors
{
    public static readonly Error NoRegistration = Error.NotFound(
        "ArrFleet.NoRegistration", "Nu există un dosar PFA pentru acest cont.");

    public static readonly Error NotFound = Error.NotFound(
        "ArrFleet.NotFound", "Pasul „ARR & Cont Flotă” nu a fost început.");

    public static readonly Error AlreadySubmitted = Error.Conflict(
        "ArrFleet.AlreadySubmitted", "Pasul a fost trimis. Îl poate redeschide doar un agent RIDElance.");

    public static Error Incomplete(IEnumerable<string> missing) => Error.Problem(
        "ArrFleet.Incomplete", $"Mai lipsește: {string.Join(", ", missing)}.");

    public static readonly Error BadgePlatformNotSelected = Error.Problem(
        "ArrFleet.BadgePlatformNotSelected", "Ecusonul se poate încărca doar pentru o platformă aleasă de client.");

    public static Error OfficialDocumentMissing(string label) => Error.Problem(
        "ArrFleet.OfficialDocumentMissing", $"Încarcă întâi documentul oficial: {label}.");

    public static readonly Error ReasonRequired = Error.Problem(
        "ArrFleet.ReasonRequired", "Scrie motivul pentru care redeschizi pasul.");

    public static readonly Error NotSubmitted = Error.Problem(
        "ArrFleet.NotSubmitted", "Clientul n-a trimis încă pasul.");
}

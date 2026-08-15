using SharedKernel;

namespace Application.Banking;

internal static class BankErrors
{
    public static readonly Error NotConfigured = Error.Problem(
        "Bank.NotConfigured",
        "Conectarea contului bancar nu este disponibilă momentan.");

    public static readonly Error AlreadyLinked = Error.Problem(
        "Bank.AlreadyLinked",
        "Ai deja un cont bancar conectat. Deconectează-l înainte să adaugi altul.");

    public static readonly Error NoConnection = Error.NotFound(
        "Bank.NoConnection",
        "Nu există o conectare bancară în curs.");
}

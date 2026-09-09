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

    public static readonly Error TermsNotAccepted = Error.Problem(
        "Bank.TermsNotAccepted",
        "Trebuie să accepți termenii serviciului de open banking înainte de conectare.");

    public static readonly Error EmailRequired = Error.Problem(
        "Bank.EmailRequired",
        "Contul tău nu are o adresă de email, iar banca o cere pentru consimțământ.");

    public static readonly Error UnknownInstitution = Error.Problem(
        "Bank.UnknownInstitution",
        "Banca aleasă nu e disponibilă momentan. Alege alta din listă.");

    public static readonly Error PsuIdRequired = Error.Problem(
        "Bank.PsuIdRequired",
        "Banca aleasă cere numele de utilizator pe care îl folosești la ea.");

    public static readonly Error PsuIdTypeRequired = Error.Problem(
        "Bank.PsuIdTypeRequired",
        "Alege dacă e cont de persoană fizică sau de firmă.");

    public static readonly Error IbanRequired = Error.Problem(
        "Bank.IbanRequired",
        "Banca aleasă cere IBAN-ul contului pentru care dai acordul.");
}

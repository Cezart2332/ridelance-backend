using Domain.Rentals;
using SharedKernel;

namespace Application.Rentals;

/// <summary>
/// Termenii contractuali ai unei închirieri, așa cum vin din formular: ca text, nu ca enum.
/// </summary>
/// <remarks>
/// În API, valorile închise (ziua de plată, modalitatea, regula de indisponibilitate) circulă ca
/// nume — <c>"Monday"</c>, <c>"BankTransfer"</c>, <c>"Proportional"</c> — la fel ca tipul
/// chiriașului și metoda unei plăți. Un nume necunoscut e o eroare, nu se ignoră: un contract
/// generat cu ziua de plată ștearsă pe tăcute e mai rău decât un formular care se plânge.
/// </remarks>
public sealed record RentalTermsInput(
    string? PaymentDueDay,
    string? PaymentMethod,
    decimal? LatePenaltyPercentPerDay,
    long? FranchiseBani,
    int? NoticeDays,
    string? UnavailabilityRule);

/// <summary>Aceiași termeni, validați.</summary>
public sealed record RentalTerms(
    DayOfWeek? PaymentDueDay,
    RentalPaymentMethod? PaymentMethod,
    decimal? LatePenaltyPercentPerDay,
    long? FranchiseBani,
    int? NoticeDays,
    RentalUnavailabilityRule? UnavailabilityRule)
{
    public static Result<RentalTerms> Parse(RentalTermsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!TryParse(input.PaymentDueDay, out DayOfWeek? day))
        {
            return Invalid("paymentDueDay", "Ziua de plată nu e recunoscută (Monday … Sunday).");
        }

        if (!TryParse(input.PaymentMethod, out RentalPaymentMethod? method))
        {
            return Invalid("paymentMethod", "Modalitatea de plată nu e recunoscută (Cash, BankTransfer, Card, Other).");
        }

        if (!TryParse(input.UnavailabilityRule, out RentalUnavailabilityRule? rule))
        {
            return Invalid("unavailabilityRule", "Regula de indisponibilitate nu e recunoscută (NotDue, Proportional, FullyDue).");
        }

        if (input.LatePenaltyPercentPerDay is < 0 or > 100)
        {
            return Invalid("latePenaltyPercentPerDay", "Penalitatea trebuie să fie între 0 și 100 % pe zi.");
        }

        if (input.FranchiseBani is < 0)
        {
            return Invalid("franchiseBani", "Franșiza nu poate fi negativă.");
        }

        if (input.NoticeDays is < 0 or > 365)
        {
            return Invalid("noticeDays", "Preavizul trebuie să fie între 0 și 365 de zile.");
        }

        return Result.Success(new RentalTerms(
            day, method, input.LatePenaltyPercentPerDay, input.FranchiseBani, input.NoticeDays, rule));
    }

    /// <summary>
    /// Un nume de enum, fără să țină cont de majuscule. Gol = nesetat (reușită); un nume care nu
    /// există, sau un număr în afara listei, e eșec.
    /// </summary>
    public static bool TryParse<T>(string? value, out T? parsed)
        where T : struct, Enum
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out T result) && Enum.IsDefined(result))
        {
            parsed = result;
            return true;
        }

        return false;
    }

    private static Result<RentalTerms> Invalid(string field, string message) =>
        Result.Failure<RentalTerms>(Error.Problem($"Rental.Invalid.{field}", message));
}

/// <summary>Curățarea textelor din formular, aceeași la creare, la corectare și la completare.</summary>
internal static class RentalInput
{
    /// <summary>Text tăiat la capete; gol = <c>null</c>.</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Lista fără intrări goale și fără dubluri.</summary>
    public static List<string> CleanList(IEnumerable<string>? values) =>
        values?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    /// <summary>
    /// Pentru corectări: <c>null</c> = câmpul n-a fost trimis, rămâne cum era; text gol = se șterge.
    /// </summary>
    /// <remarks>
    /// Câmpurile noi ale contractului nu se pot trata ca restul formularului de corectare, care se
    /// trimite întreg: un formular vechi, care nu le știe, le-ar fi șters la fiecare salvare.
    /// </remarks>
    public static string? Patch(string? current, string? sent) =>
        sent is null ? current : Clean(sent);
}

namespace Domain.Rentals;

/// <summary>
/// Ce s-a decis despre închiriere. Nu ce arată calendarul.
/// </summary>
/// <remarks>
/// Specul cerea un singur status cu cinci valori: draft, upcoming, active, completed, cancelled.
/// Trei dintre ele — upcoming, active, completed — se citesc din date și din momentul închiderii,
/// iar stocate ar fi cerut un job la fiecare miezul nopții și ar fi mințit între rulări. Rămân
/// derivate, cum erau (`RentalStatus.For`).
///
/// Aici stau doar cele două care nu se pot deduce din nimic, pentru că sunt decizii ale cuiva:
/// o închiriere pregătită dar neconfirmată, și una anulată înainte să înceapă.
/// </remarks>
public enum RentalLifecycle
{
    /// <summary>Pregătită, încă neconfirmată. Nu blochează mașina.</summary>
    Draft,

    /// <summary>Confirmată. De aici încolo starea o dau datele.</summary>
    Confirmed,

    /// <summary>Anulată. Rămâne pentru istoric, dar nu s-a întâmplat.</summary>
    Cancelled,
}

using System.Linq.Expressions;
using Domain.Cars;

namespace Application.Cars;

/// <summary>
/// Condiția „anunțul se vede în marketplace", scrisă o singură dată.
/// </summary>
/// <remarks>
/// Perechea expresiei cu <see cref="Car.Active"/>: aceeași regulă, în două forme. Proprietatea o
/// evaluează pe un obiect încărcat; expresia se traduce în SQL, pentru filtrare în bază.
///
/// Trebuie ținute identice. Dacă regula se schimbă, se schimbă în ambele — de aceea stau
/// documentate una lângă alta, nu în fișiere diferite.
/// </remarks>
public static class CarVisibility
{
    public static readonly Expression<Func<Car, bool>> IsPublic = car =>
        car.ListingStatus == ListingStatus.Published
        && car.ApprovalStatus == CarApprovalStatus.Approved
        && (car.PaymentStatus == CarListingPaymentStatus.Paid
            || car.PaymentStatus == CarListingPaymentStatus.NotRequired);
}

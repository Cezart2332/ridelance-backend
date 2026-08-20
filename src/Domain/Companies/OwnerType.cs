namespace Domain.Companies;

/// <summary>
/// Entitatea din spatele unui cont care listează mașini.
/// </summary>
/// <remarks>
/// Deliberat separat de <see cref="Users.UserRole"/>. Spec-ul §1.1 cerea redenumirea rolului în
/// <c>Pfa</c>/<c>Srl</c>, dar <c>CarPoster</c> decide permisiuni, accesul la mașini și dacă
/// listarea se plătește: redenumirea lui ar fi transformat un refactor de UI într-o schimbare de
/// autorizare și de plăți, fără câștig.
///
/// Rolul rămâne mecanismul de acces; tipul ăsta e citirea de business a aceleiași informații.
/// </remarks>
public enum OwnerType
{
    Pfa = 0,
    Srl = 1,
}

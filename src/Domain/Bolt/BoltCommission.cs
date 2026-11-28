namespace Domain.Bolt;

/// <summary>
/// Comisionul Bolt pe cursă. Fleet API 2.0 trimite doar brutul (<c>price.total</c>), fără net și
/// comision, așa că îl calculăm: 25% din brut, fără bacșiș, pe care Bolt nu ia comision.
/// Venitul declarat rămâne brutul; comisionul real, deductibil, vine din factura lunară Bolt.
/// </summary>
public static class BoltCommission
{
    public const decimal Rate = 0.25m;

    public static decimal Of(decimal ridePrice, decimal tip) =>
        Math.Round(Math.Max(0m, ridePrice - tip) * Rate, 2, MidpointRounding.AwayFromZero);
}

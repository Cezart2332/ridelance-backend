namespace Domain.Payments;

/// <summary>
/// A discount code as it exists in Stripe: a promotion code (the string customers type)
/// backed by a coupon (the actual reduction). Stripe owns the redemption counter, so nothing
/// about this is stored locally.
/// </summary>
/// <param name="Id">Stripe promotion code ID.</param>
/// <param name="Code">The code customers type at checkout, e.g. "RIDE20".</param>
/// <param name="AmountOffBani">Fixed reduction in bani, when the code is an amount discount.</param>
/// <param name="PercentOff">Percentage reduction, when the code is a percentage discount.</param>
/// <param name="Currency">Currency of <paramref name="AmountOffBani"/>; null for percentage codes.</param>
/// <param name="MaxRedemptions">How many times the code may be used in total; null means unlimited.</param>
/// <param name="TimesRedeemed">How many times it has been used so far.</param>
/// <param name="Active">Whether the code can still be applied.</param>
/// <param name="AppliesToAllPayments">
/// For subscriptions: <see langword="true"/> discounts every invoice, <see langword="false"/> only the first one.
/// </param>
/// <param name="ExpiresAtUtc">Optional expiry date.</param>
/// <param name="CreatedAtUtc">When the code was created.</param>
public sealed record DiscountCode(
    string Id,
    string Code,
    long? AmountOffBani,
    decimal? PercentOff,
    string? Currency,
    long? MaxRedemptions,
    long TimesRedeemed,
    bool Active,
    bool AppliesToAllPayments,
    DateTime? ExpiresAtUtc,
    DateTime CreatedAtUtc);

/// <summary>
/// What an admin fills in to create a discount code. Exactly one of
/// <see cref="AmountOffBani"/> and <see cref="PercentOff"/> must be set.
/// </summary>
public sealed record NewDiscountCode(
    string Code,
    long? AmountOffBani,
    decimal? PercentOff,
    long? MaxRedemptions,
    bool AppliesToAllPayments,
    DateTime? ExpiresAtUtc);

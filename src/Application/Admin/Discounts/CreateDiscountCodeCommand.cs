using System.Text.RegularExpressions;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Payments;
using SharedKernel;

namespace Application.Admin.Discounts;

/// <summary>
/// Creates a discount code. Either a fixed amount in bani or a percentage, never both.
/// </summary>
public sealed record CreateDiscountCodeCommand(
    string Code,
    long? AmountOffBani,
    decimal? PercentOff,
    long? MaxRedemptions,
    bool AppliesToAllPayments,
    DateTime? ExpiresAtUtc) : ICommand<DiscountCode>;

internal sealed partial class CreateDiscountCodeCommandHandler(IStripeService stripeService)
    : ICommandHandler<CreateDiscountCodeCommand, DiscountCode>
{
    /// <summary>Stripe only accepts letters, digits and dashes in a promotion code.</summary>
    [GeneratedRegex("^[A-Z0-9-]+$")]
    private static partial Regex AllowedCodeCharacters { get; }

    public async Task<Result<DiscountCode>> Handle(
        CreateDiscountCodeCommand command,
        CancellationToken cancellationToken)
    {
        string code = (command.Code ?? string.Empty).Trim().ToUpperInvariant();

        if (code.Length == 0)
        {
            return Result.Failure<DiscountCode>(Error.Problem(
                "Discount.CodeMissing",
                "Codul de reducere este obligatoriu."));
        }

        if (!AllowedCodeCharacters.IsMatch(code))
        {
            return Result.Failure<DiscountCode>(Error.Problem(
                "Discount.InvalidCode",
                "Codul poate conține doar litere, cifre și liniuțe."));
        }

        if (command.AmountOffBani.HasValue == command.PercentOff.HasValue)
        {
            return Result.Failure<DiscountCode>(Error.Problem(
                "Discount.InvalidValue",
                "Alege fie o sumă fixă, fie un procent — nu ambele."));
        }

        if (command.PercentOff is { } percent && (percent <= 0 || percent > 100))
        {
            return Result.Failure<DiscountCode>(Error.Problem(
                "Discount.InvalidPercent",
                "Procentul trebuie să fie între 1 și 100."));
        }

        if (command.AmountOffBani is { } amount && amount <= 0)
        {
            return Result.Failure<DiscountCode>(Error.Problem(
                "Discount.InvalidAmount",
                "Suma redusă trebuie să fie mai mare decât zero."));
        }

        if (command.MaxRedemptions is { } max && max < 1)
        {
            return Result.Failure<DiscountCode>(Error.Problem(
                "Discount.InvalidMaxRedemptions",
                "Numărul maxim de utilizări trebuie să fie cel puțin 1."));
        }

        if (command.ExpiresAtUtc is { } expiry && expiry <= DateTime.UtcNow)
        {
            return Result.Failure<DiscountCode>(Error.Problem(
                "Discount.InvalidExpiry",
                "Data de expirare trebuie să fie în viitor."));
        }

        var newCode = new NewDiscountCode(
            code,
            command.AmountOffBani,
            command.PercentOff,
            command.MaxRedemptions,
            command.AppliesToAllPayments,
            command.ExpiresAtUtc);

        DiscountCode created = await stripeService.CreateDiscountCodeAsync(newCode, cancellationToken);
        return Result.Success(created);
    }
}

using SharedKernel;

namespace Domain.Rentals;

/// <summary>Cum a intrat banul. Se alege dintr-o listă închisă, ca să se poată număra.</summary>
public enum RentalPaymentMethod
{
    Cash,
    BankTransfer,
    Card,
    Other,
}

/// <summary>
/// O plată încasată de la chiriaș, înregistrată de proprietar.
/// </summary>
/// <remarks>
/// Se **înregistrează**, nu se încasează prin platformă: banii trec direct între flotă și chiriaș,
/// iar RIDElance ține doar evidența. De aceea nu există status de plată și nici legătură cu Stripe
/// — ar sugera o încasare pe care n-o facem noi.
///
/// Din același motiv, nicăieri în rezumat nu se scrie „profit" sau „câștig" (spec §10): n-avem
/// sursa completă a cheltuielilor, deci orice cifră numită așa ar fi o minciună convenabilă.
/// </remarks>
public sealed class RentalPayment : Entity
{
    public Guid Id { get; set; }

    public Guid RentalId { get; set; }
    public Rental Rental { get; set; } = null!;

    public long AmountBani { get; set; }

    /// <summary>Când s-a încasat, nu când s-a înregistrat. Se completează retroactiv des.</summary>
    public DateTime PaidOnUtc { get; set; }

    public RentalPaymentMethod Method { get; set; } = RentalPaymentMethod.BankTransfer;

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

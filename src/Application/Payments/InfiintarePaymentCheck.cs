using Application.Abstractions.Data;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Application.Payments;

public static class InfiintarePaymentCheck
{
    /// <summary>
    /// Heuristică partajată pentru „a plătit înființarea PFA” — folosită și de
    /// GetSubscription și de starea de onboarding, ca să nu diveargă.
    /// </summary>
    public static Task<bool> HasPaidAsync(
        IApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return context.PaymentRecords
            .AnyAsync(r => r.UserId == userId &&
                           r.PaymentType == PaymentType.OneTime &&
                           r.Status == PaymentStatus.Succeeded &&
                           (r.Description.Contains("pfa") ||
                            r.Description.Contains("înființare") ||
                            r.Description.Contains("infiintare") ||
                            r.Description.Contains("Serviciu") ||
                            r.AmountBani == 30000 ||
                            r.AmountBani == 45000 ||
                            r.AmountBani == 79900),
                      cancellationToken);
    }
}

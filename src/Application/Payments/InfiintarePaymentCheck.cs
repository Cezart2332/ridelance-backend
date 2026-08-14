using Application.Abstractions.Data;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Application.Payments;

public static class InfiintarePaymentCheck
{
    /// <summary>
    /// „A plătit înființarea PFA?” — folosită și de GetSubscription, și de starea de onboarding,
    /// ca cele două să nu diveargă.
    ///
    /// Se răspunde întâi din legătura reală (<see cref="PaymentRecord.PfaRegistrationId"/>). Cât
    /// timp mai există plăți vechi nelegate, se cade pe euristica istorică pe descriere și sumă.
    /// Fallbackul e de scos după ce backfillul acoperă tot.
    /// </summary>
    public static async Task<bool> HasPaidAsync(
        IApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        bool linked = await context.PaymentRecords
            .AnyAsync(r => r.UserId == userId &&
                           r.PfaRegistrationId != null &&
                           r.PaymentType == PaymentType.OneTime &&
                           r.Status == PaymentStatus.Succeeded,
                      cancellationToken);

        if (linked)
        {
            return true;
        }

        return await context.PaymentRecords
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

    /// <summary>
    /// Ultima încercare de plată a înființării a eșuat și nu există niciuna reușită — ecranul de
    /// plată trebuie să ofere reîncercare, nu să pară că nu s-a întâmplat nimic.
    /// </summary>
    public static async Task<bool> HasFailedAttemptAsync(
        IApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken) =>
        await context.PaymentRecords
            .AnyAsync(r => r.UserId == userId &&
                           r.PaymentType == PaymentType.OneTime &&
                           r.Status == PaymentStatus.Failed,
                      cancellationToken);
}

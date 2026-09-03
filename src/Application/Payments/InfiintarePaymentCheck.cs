using Application.Abstractions.Data;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Application.Payments;

public static class InfiintarePaymentCheck
{
    /// <summary>
    /// „A plătit avansul?” — folosită și de GetSubscription, și de starea de onboarding, ca cele
    /// două să nu diveargă.
    ///
    /// Trei surse, în ordinea încrederii. Descrierea NU e o euristică pentru plățile noi: e
    /// răspunsul principal. Avansul se cere înaintea întrebării „ai deja PFA?", deci rândul se
    /// naște fără <c>PfaRegistrationId</c> și rămâne așa până se deschide dosarul — dacă am fi
    /// cerut legătura, clientul ar fi fost pus să plătească a doua oară exact între cele două
    /// ecrane. Ultimul bloc rămâne euristica istorică, pentru plățile de dinaintea constantelor.
    /// </summary>
    public static async Task<bool> HasPaidAsync(
        IApplicationDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        bool known = await context.PaymentRecords
            .AnyAsync(r => r.UserId == userId &&
                           r.PaymentType == PaymentType.OneTime &&
                           r.Status == PaymentStatus.Succeeded &&
                           (r.PfaRegistrationId != null ||
                            r.Description == Pricing.RidelanceStart.OnboardingAdvanceDescription ||
                            r.Description == Pricing.RidelanceStart.LegacyInfiintareDescription),
                      cancellationToken);

        if (known)
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

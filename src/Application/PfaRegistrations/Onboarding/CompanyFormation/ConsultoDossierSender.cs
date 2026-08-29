using Application.Abstractions;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.PfaRegistrations.Onboarding.Notifications;
using Domain.PfaRegistrations.CompanyFormation;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

/// <summary>
/// Singurul drum pe care dosarul de înființare pleacă spre Consulto.
///
/// Există un singur loc pentru că poarta e una singură: <c>plata_confirmata</c>. Webhook-ul
/// Stripe îl folosește pe traseul normal, iar adminul îl folosește pentru reluarea unei
/// trimiteri care a picat (arhivă negenerabilă, email respins). Dacă fiecare și-ar fi scris
/// propria variantă, una din ele ar fi ajuns să trimită fără plată — exact defectul raportat.
///
/// Idempotent pe perechea (event Stripe, dosar): <see cref="CompanyFormationRequest.SentToConsultoAtUtc"/>
/// e stampila care oprește a doua trimitere, indiferent de câte ori reîncearcă Stripe.
/// </summary>
public sealed class ConsultoDossierSender(
    IApplicationDbContext context,
    OnboardingOpsNotifier opsNotifier,
    IQueryHandler<ExportCompanyFormationQuery, CompanyFormationExport> companyFormationExport,
    ILogger<ConsultoDossierSender> logger)
{
    /// <summary>
    /// Trimite arhiva, dacă dosarul are voie. Întoarce <see cref="ErrorType.Conflict"/> (409)
    /// pentru orice dosar care nu e în <c>plata_confirmata</c> — inclusiv pentru unul deja
    /// trimis, ca o a doua apăsare pe buton să nu producă un al doilea email.
    /// </summary>
    public async Task<Result> SendAsync(
        Guid pfaRegistrationId,
        long amountBani,
        string? stripeEventId,
        CancellationToken cancellationToken)
    {
        CompanyFormationRequest? request = await context.CompanyFormationRequests
            .Include(r => r.Signature)
            .FirstOrDefaultAsync(r => r.PfaRegistrationId == pfaRegistrationId, cancellationToken);

        if (request is null)
        {
            return Result.Failure(CompanyFormationErrors.NoRegistration);
        }

        if (request.SentToConsultoAtUtc is not null)
        {
            return Result.Failure(CompanyFormationErrors.AlreadySentToConsulto);
        }

        if (!request.CanSendToConsulto)
        {
            return Result.Failure(CompanyFormationErrors.PaymentNotConfirmed);
        }

        if (request.Signature is null)
        {
            return Result.Failure(CompanyFormationErrors.NotSigned);
        }

        // Prin înregistrare, nu prin navigația `request.PfaRegistration`: aceasta nu e inclusă
        // în query-ul de mai sus, deci ar fi null.
        User? user = await context.PfaRegistrations
            .AsNoTracking()
            .Where(r => r.Id == pfaRegistrationId)
            .Select(r => r.User)
            .FirstOrDefaultAsync(cancellationToken);

        Result<CompanyFormationExport> export = await companyFormationExport.Handle(
            new ExportCompanyFormationQuery(pfaRegistrationId), cancellationToken);

        if (export.IsFailure)
        {
            logger.LogWarning(
                "Arhiva dosarului {PfaRegistrationId} nu a putut fi generată: {Error}.",
                pfaRegistrationId,
                export.Error.Description);

            return Result.Failure(export.Error);
        }

        string applicant = $"{request.Solicitant.Nume} {request.Solicitant.Prenume}".Trim();
        if (string.IsNullOrWhiteSpace(applicant) && user is not null)
        {
            applicant = $"{user.FirstName} {user.LastName}".Trim();
        }

        await opsNotifier.PfaDossierReadyAsync(
            applicant,
            user?.Email ?? "—",
            request.Signature.SignedAtUtc,
            amountBani,
            new EmailAttachmentContent(export.Value.FileName, "application/zip", export.Value.Content),
            cancellationToken);

        request.Status = CompanyFormationStatus.SentToConsulto;
        request.SentToConsultoAtUtc = DateTime.UtcNow;
        request.ConsultoSendStripeEventId = stripeEventId;
        request.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Marchează plata confirmată, fără să dea înapoi un dosar pe care adminul l-a dus deja mai
    /// departe: din <c>Approved</c> nu se coboară în <c>PaymentConfirmed</c>.
    /// </summary>
    public static void MarkPaymentConfirmed(CompanyFormationRequest request)
    {
        request.PaymentConfirmedAtUtc ??= DateTime.UtcNow;

        if (request.Status is CompanyFormationStatus.Draft
            or CompanyFormationStatus.AwaitingPayment
            or CompanyFormationStatus.Submitted)
        {
            request.Status = CompanyFormationStatus.PaymentConfirmed;
        }

        request.UpdatedAtUtc = DateTime.UtcNow;
    }
}

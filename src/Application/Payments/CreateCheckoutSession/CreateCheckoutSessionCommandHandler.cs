using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Application.PfaRegistrations.Onboarding;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.PfaRegistrations.CompanyFormation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Payments.CreateCheckoutSession;

internal sealed class CreateCheckoutSessionCommandHandler(
    IStripeService stripeService,
    IApplicationDbContext context,
    IConfiguration configuration)
    : ICommandHandler<CreateCheckoutSessionCommand, string>
{
    public async Task<Result<string>> Handle(
        CreateCheckoutSessionCommand command,
        CancellationToken cancellationToken)
    {
        // Nu mai există „o schimbare pe săptămână": poarta aia păzea ancora de luni 15:00, iar
        // schimbarea de plan se încasează acum, la checkout, ca orice altă plată.

#pragma warning disable S1075 // URIs should not be hardcoded
        string baseUrl = configuration["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl is missing in configuration.");
#pragma warning restore S1075

        // Build success/cancel URLs
        // Subscription: after paying, user must still complete the PFA step
        // Payment (Infiintare PFA): goes to the registration success page
        string successUrl = command.SuccessUrl ?? (command.Mode == "subscription"
            ? $"{baseUrl}/inregistrare/pfa?subscribed=1&session_id={{CHECKOUT_SESSION_ID}}&plan={command.Plan}"
            : $"{baseUrl}/inregistrare/succes?session_id={{CHECKOUT_SESSION_ID}}");

        string cancelUrl = command.CancelUrl ?? (command.Mode == "subscription"
            ? $"{baseUrl}/inregistrare/abonament"
            : $"{baseUrl}/inregistrare/pfa");

        // Metadata: planul, plus ciclul pe abonamente. Webhookul are nevoie de ciclu ca să știe
        // ce a cumpărat clientul — prețul Stripe îl spune, dar nu ajunge întreg în eveniment.
        // Bifa BCR călătorește pe același drum ca planul și ciclul. Nu se scrie direct în baza de
        // date la crearea sesiunii: o sesiune de checkout poate fi abandonată, iar o intenție
        // înregistrată pentru o plată care nu s-a făcut ar cere reducere pentru un abonament
        // inexistent. Ajunge în abonament din webhook, adică după ce s-a plătit.
        string bcrMark = command.BcrDiscountRequested ? "|bcr:1" : string.Empty;
        string metadata = command.Mode == "subscription"
            ? $"plan:{command.Plan}|cycle:{command.Cycle}{bcrMark}"
            : $"plan:{command.Plan}";



        IReadOnlyDictionary<string, string>? sessionMetadata = command.IsPlanChange
            ? new Dictionary<string, string> { ["isPlanChange"] = "true" }
            : null;

        if (!StripeCatalog.TryResolvePlan(command.Plan, command.Mode, command.Cycle, out StripeCatalogItem? catalogItem))
        {
            return Result.Failure<string>(Error.Problem(
                "Checkout.UnknownPlan",
                "Planul selectat nu este disponibil."));
        }

        // RL-03 — taxa de înființare se încasează doar pe un dosar care poate fi depus. Poarta stă
        // aici, nu doar în UI: altfel un apel direct la API ar crea un intent pe date incomplete.
        Guid? pfaRegistrationId = null;

        if (IsInfiintareOnboarding(command))
        {
            Result<Guid> gate = await EnsurePayableAsync(command.UserId, cancellationToken);

            if (gate.IsFailure)
            {
                return Result.Failure<string>(gate.Error);
            }

            pfaRegistrationId = gate.Value;
        }

        if (pfaRegistrationId is not null)
        {
            var withPfa = new Dictionary<string, string>(sessionMetadata ?? new Dictionary<string, string>())
            {
                // Webhookul leagă plata de dosar din asta, în loc să ghicească din descriere.
                ["pfaRegistrationId"] = pfaRegistrationId.Value.ToString(),
            };
            sessionMetadata = withPfa;
        }

        string priceId = await stripeService.ResolvePriceIdAsync(catalogItem, cancellationToken);

        // Avansul plătit în onboarding se întoarce aici, ca reducere pe primele facturi.
        string? couponId = await AdvanceCreditCouponAsync(command, cancellationToken);

        string sessionUrl = await stripeService.CreateCheckoutSessionAsync(
            priceId,
            command.Mode,
            successUrl,
            cancelUrl,
            command.UserEmail,
            command.UserId.ToString(),
            metadata,
            sessionMetadata,
            // Dublu-click pe „Plătește” nu are voie să producă două sesiuni. Cheia e stabilă cât
            // timp dosarul și planul sunt aceleași, deci reîncercarea reia aceeași plată.
            pfaRegistrationId is null ? null : $"infiintare:{pfaRegistrationId}:{command.Plan}",
            couponId,
            cancellationToken);

        return sessionUrl;
    }

    private static bool IsInfiintareOnboarding(CreateCheckoutSessionCommand command) =>
        command.Mode == "payment"
        && command.Plan.Equals("infiintare_pfa", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Întoarce dosarul plătibil, sau 422 cu ce mai lipsește. Mesajul e cel arătat clientului,
    /// deci enumeră etapele, nu numele câmpurilor din DB.
    /// </summary>
    private async Task<Result<Guid>> EnsurePayableAsync(Guid userId, CancellationToken cancellationToken)
    {
        PfaRegistration? registration = await context.PfaRegistrations
            .AsNoTracking()
            .Include(r => r.CompanyFormationRequest)
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return Result.Failure<Guid>(Error.Unprocessable(
                "Checkout.NoRegistration",
                "Deschide întâi dosarul de înființare."));
        }

        // §13.5 — o sesiune atinsă de uneltele de dezvoltare nu are voie să genereze o plată
        // reală. Poarta e aici, nu în UI: butonul se poate ascunde, endpoint-ul nu.
        if (registration.IsDevSession)
        {
            return Result.Failure<Guid>(Error.Unprocessable(
                "Checkout.DevSession",
                "Sesiune de test: plățile reale sunt dezactivate. Folosește Stripe în test mode."));
        }

        bool hasPaid = await InfiintarePaymentCheck.HasPaidAsync(context, userId, cancellationToken);

        if (hasPaid)
        {
            return Result.Failure<Guid>(Error.Unprocessable(
                "Checkout.AlreadyPaid",
                "Înființarea este deja achitată."));
        }

        // Avansul se datorează pe ambele ramuri, deci după poarta de mai sus nu mai rămâne niciun
        // caz de respins: un dosar care există și n-a plătit e plătibil.
        return OnboardingStateBuilder.CanPayOnboardingAdvance(registration, hasPaid)
            ? Result.Success(registration.Id)
            : Result.Failure<Guid>(Error.Unprocessable(
                "Checkout.NotApplicable",
                "Avansul nu poate fi plătit acum."));
    }

    /// <summary>
    /// Cuponul cu care avansul plătit în onboarding se întoarce la primul abonament — sau
    /// <c>null</c> când nu se aplică.
    ///
    /// Două condiții, amândouă necesare: avansul e plătit ȘI clientul n-a mai avut niciun
    /// abonament. A doua ține loc de „creditul s-a consumat deja", fără o coloană nouă: rândul de
    /// <c>UserSubscription</c> rămâne și după anulare, deci reducerea nu se poate lua a doua oară
    /// printr-un abonament reluat, iar o schimbare de plan nu o reia nici ea.
    /// </summary>
    private async Task<string?> AdvanceCreditCouponAsync(
        CreateCheckoutSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Mode != "subscription")
        {
            return null;
        }

        if (Pricing.OnboardingAdvanceCredit.For(command.Plan) is not { } spec)
        {
            return null;
        }

        bool hasSubscribedBefore = await context.UserSubscriptions
            .AnyAsync(s => s.UserId == command.UserId, cancellationToken);

        if (hasSubscribedBefore)
        {
            return null;
        }

        return await InfiintarePaymentCheck.HasPaidAsync(context, command.UserId, cancellationToken)
            ? await stripeService.EnsureAdvanceCreditCouponAsync(spec, cancellationToken)
            : null;
    }
}

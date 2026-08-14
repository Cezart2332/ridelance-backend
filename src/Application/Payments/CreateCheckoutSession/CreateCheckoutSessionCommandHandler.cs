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
        if (command.IsPlanChange)
        {
            UserSubscription? subscription = await context.UserSubscriptions
                .Where(s => s.UserId == command.UserId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (subscription is not null && subscription.Status == SubscriptionStatus.ActivePendingBilling)
            {
                return Result.Failure<string>(Error.Problem(
                    "Subscription.PendingChange",
                    "Ai deja o schimbare de abonament programată pentru lunea viitoare. Poți schimba abonamentul doar o dată pe săptămână."));
            }
        }

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

        // Build metadata: include plan/service name + billing anchor for subscriptions
        string metadata = command.Mode == "subscription" && command.BillingAnchorUnix.HasValue
            ? $"plan:{command.Plan}|billingAnchor:{command.BillingAnchorUnix.Value}"
            : $"plan:{command.Plan}";



        IReadOnlyDictionary<string, string>? sessionMetadata = command.IsPlanChange
            ? new Dictionary<string, string> { ["isPlanChange"] = "true" }
            : null;

        if (!StripeCatalog.TryResolvePlan(command.Plan, command.Mode, out StripeCatalogItem? catalogItem))
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

        bool hasPaid = await InfiintarePaymentCheck.HasPaidAsync(context, userId, cancellationToken);

        if (hasPaid)
        {
            return Result.Failure<Guid>(Error.Unprocessable(
                "Checkout.AlreadyPaid",
                "Înființarea este deja achitată."));
        }

        if (OnboardingStateBuilder.CanPayInfiintare(registration, hasPaid))
        {
            return Result.Success(registration.Id);
        }

        CompanyFormationRequest? formation = registration.CompanyFormationRequest;
        var missing = new List<string>();

        if (formation is null || !formation.PersonalDataComplete)
        {
            missing.Add("datele personale");
        }

        if (formation is null || !formation.RegisteredOfficeComplete)
        {
            missing.Add("sediul profesional");
        }

        missing.Add("semnarea dosarului");

        return Result.Failure<Guid>(Error.Unprocessable(
            "Checkout.FormationIncomplete",
            $"Dosarul nu poate fi depus încă. Mai lipsește: {string.Join(", ", missing)}."));
    }
}

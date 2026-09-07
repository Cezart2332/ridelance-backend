using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Services;
using Domain.Companies;
using Domain.Payments;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Companies.Onboarding;

public sealed record FleetOnboardingInput(
    int Step, string? Cui = null, bool ConfirmCompany = false,
    string? FirstName = null, string? LastName = null, string? Position = null,
    string? OtherPosition = null, string[]? Platforms = null, int VehicleCount = 0,
    bool Deferred = false, bool BcrRequested = false, string? Cycle = null,
    bool TermsAccepted = false, bool PrivacyAccepted = false);

/// <param name="ContactVerificationRequired">
/// Confirmarea emailului și a telefonului blochează pasul. Fals cât timp furnizorii de email și
/// SMS nu sunt configurați: altfel pasul ar cere un cod care nu poate ajunge nicăieri, iar
/// înrolarea s-ar opri acolo definitiv. Se aprinde din configurație, nu dintr-o modificare de cod.
/// </param>
public sealed record FleetOnboardingResponse(
    FleetOnboarding Progress, bool DashboardAllowed, string Email, string? Phone,
    bool EmailVerified, bool PhoneVerified, string FirstName, string LastName,
    bool BankConnected, bool OblioConnected, long AmountDueBani, long RegularAmountBani,
    long MonthlyAmountBani, long AnnualAmountBani, bool LegacyAccount,
    bool ContactVerificationRequired = false);

public sealed class FleetOnboardingService(
    IApplicationDbContext context, IUserContext userContext,
    ICompanyLookupService lookup, IStripeService stripe, IConfiguration configuration)
{
    private static readonly HashSet<string> Positions = ["Administrator", "Asociat", "Manager flotă", "Manager operațional", "Angajat", "Altă funcție"];
    private static readonly HashSet<string> Platforms = ["Uber", "Bolt", "Blue", "BlackCab", "Altele", "Niciuna"];

    /// <summary>
    /// Confirmarea contactelor e obligatorie doar când chiar putem trimite coduri.
    ///
    /// Implicit FALS: furnizorii de email și SMS nu sunt încă configurați, iar o poartă care cere
    /// un cod ce nu poate fi livrat nu e o verificare, e un zid. Câmpurile rămân pe ecran și
    /// verificarea funcționează pentru cine o face — doar că nu mai oprește înrolarea.
    /// </summary>
    private bool ContactVerificationRequired =>
        bool.TryParse(configuration["Onboarding:RequireContactVerification"], out bool required) && required;

    public async Task<Result<FleetOnboardingResponse>> CancelCheckoutAsync(CancellationToken ct)
    {
        User? user = await UserAsync(ct);
        if (user is null)
        {
            return Failure("Contul nu este de tip SRL.");
        }

        FleetOnboarding p = user.FleetOnboarding;
        if (p.CheckoutClientSecret is null && p.CheckoutAttemptId is not null)
        {
            return Failure("Reia sesiunea de plată înainte de anulare.");
        }

        if (p.CheckoutClientSecret is not null)
        {
            string sessionId = p.CheckoutClientSecret.Split("_secret_", StringSplitOptions.None)[0];
            (string status, _) = await stripe.GetSessionStatusAsync(sessionId, ct);
            if (status == "complete")
            {
                return Failure("Plata a fost trimisă. Așteptăm confirmarea Stripe.");
            }

            if (status == "open")
            {
                await stripe.ExpireCheckoutAsync(sessionId, ct);
            }
        }
        p.CheckoutClientSecret = null;
        p.CheckoutAttemptId = null;
        await context.SaveChangesAsync(ct);
        return await ResponseAsync(user, ct);
    }

    public async Task<Result<FleetOnboardingResponse>> GetAsync(CancellationToken ct)
    {
        User? user = await UserAsync(ct);
        return user is null ? Failure("Contul nu este de tip SRL.") : await ResponseAsync(user, ct);
    }

    public async Task<Result<FleetOnboardingResponse>> SaveAsync(FleetOnboardingInput input, CancellationToken ct)
    {
        User? user = await UserAsync(ct);
        if (user is null)
        {
            return Failure("Contul nu este de tip SRL.");
        }

        FleetOnboarding progress = user.FleetOnboarding;
        if (progress.CheckoutAttemptId is not null)
        {
            return Failure("O plată este deja în curs. Reia plata sau așteaptă expirarea sesiunii înainte să modifici datele.");
        }
        if (progress.CompletedAtUtc is not null && input.Step < 6)
        {
            return Failure("Configurarea este deja finalizată.");
        }

        if (input.Step < 1 || input.Step > 7 || input.Step > progress.CompletedStep + 1)
        {
            return Failure("Completează mai întâi pasul anterior.");
        }

        switch (input.Step)
        {
            case 1:
                if (!input.ConfirmCompany)
                {
                    string cui = (input.Cui ?? string.Empty).Trim().ToUpperInvariant();
                    if (cui.StartsWith("RO", StringComparison.Ordinal))
                    {
                        cui = cui[2..];
                    }

                    if (cui.Length is < 2 or > 10 || !cui.All(char.IsAsciiDigit))
                    {
                        return Failure("Introdu un CUI valid.");
                    }

                    CompanyLookupResult? found;
                    try
                    {
                        found = await lookup.FindForOnboardingAsync(cui, ct);
                    }
                    catch (HttpRequestException)
                    {
                        return Failure("Serviciul ANAF este temporar indisponibil. Încearcă din nou în câteva momente.");
                    }
                    if (found is null)
                    {
                        return Failure("Firma nu a fost găsită. Verifică CUI-ul și încearcă din nou.");
                    }

                    progress.PendingCompany = new(found.Cui, found.Name, found.Address, found.City, found.County,
                        found.RegistrationNumber, found.VatPayer, found.PostalCode, found.Caen,
                        found.RegistrationDate, found.Status, found.VatOnCollection);
                    await context.SaveChangesAsync(ct);
                    return await ResponseAsync(user, ct);
                }
                if (progress.PendingCompany is null)
                {
                    return Failure("Verifică firma înainte de confirmare.");
                }

                if (progress.Company is not null && progress.Company.Cui != progress.PendingCompany.Cui)
                {
                    return Failure("Firma a fost deja confirmată pentru acest cont.");
                }

                progress.Company = progress.PendingCompany;
                await SaveCompanyAsync(user, progress.Company, ct);
                break;
            case 2:
                if (ContactVerificationRequired && (!user.IsEmailVerified || !user.IsPhoneVerified))
                {
                    return Failure("Confirmă emailul și telefonul înainte de continuare.");
                }

                if (string.IsNullOrWhiteSpace(input.FirstName) || input.FirstName.Length > 128
                    || string.IsNullOrWhiteSpace(input.LastName) || input.LastName.Length > 128
                    || !Positions.Contains(input.Position ?? string.Empty))
                {
                    return Failure("Completează numele și funcția în companie.");
                }

                string? position = input.Position == "Altă funcție" ? input.OtherPosition?.Trim() : input.Position;
                if (string.IsNullOrWhiteSpace(position) || position.Length > 128)
                {
                    return Failure("Completează funcția în companie.");
                }

                user.FirstName = input.FirstName.Trim();
                user.LastName = input.LastName.Trim();
                progress.Position = position;
                break;
            case 3:
                if (input.Platforms is not { Length: > 0 and <= 7 } || input.Platforms.Any(p => !Platforms.Contains(p))
                    || input.Platforms.Contains("Niciuna") && input.Platforms.Length != 1
                    || input.VehicleCount is < 0 or > 1_000_000)
                {
                    return Failure("Selectează platformele și un număr valid de mașini.");
                }

                progress.Platforms = input.Platforms.Distinct().ToArray();
                progress.VehicleCount = input.VehicleCount;
                break;
            case 4:
                if (!input.Deferred && !await BankConnectedAsync(user.Id, ct))
                {
                    return Failure("Conectează banca sau alege configurarea mai târziu.");
                }

                progress.BankDeferred = input.Deferred;
                progress.BcrRequested = input.BcrRequested;
                break;
            case 5:
                if (!input.Deferred && !await OblioConnectedAsync(user, ct))
                {
                    return Failure("Conectează Oblio pentru firma confirmată sau alege configurarea mai târziu.");
                }

                progress.OblioDeferred = input.Deferred;
                break;
            case 6:
                if (!Enum.TryParse(input.Cycle, out SubscriptionBillingCycle cycle) || !Enum.IsDefined(cycle))
                {
                    return Failure("Alege plata lunară sau anuală.");
                }

                progress.Cycle = cycle;
                break;
            case 7:
                if (!input.TermsAccepted || !input.PrivacyAccepted)
                {
                    return Failure("Confirmă documentele juridice înainte de plată.");
                }

                progress.TermsAcceptedAtUtc = DateTime.UtcNow;
                progress.LegalVersion = FleetPricing.LegalVersion;
                break;
        }
        progress.CompletedStep = Math.Max(progress.CompletedStep, input.Step);
        await context.SaveChangesAsync(ct);
        return await ResponseAsync(user, ct);
    }

    public async Task<Result<string>> CheckoutAsync(CancellationToken ct)
    {
        User? user = await UserAsync(ct);
        if (user is null || user.FleetOnboarding.CompletedStep != 7
            || user.FleetOnboarding.TermsAcceptedAtUtc is null
            || ContactVerificationRequired && (!user.IsEmailVerified || !user.IsPhoneVerified))
        {
            return Result.Failure<string>(Error.Unprocessable("Fleet.Incomplete", "Completează configurarea înainte de plată."));
        }

        if (await context.UserSubscriptions.AnyAsync(s => s.UserId == user.Id
            && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired, ct))
        {
            return Result.Failure<string>(Error.Conflict("Fleet.Subscribed", "Există deja un abonament. Dacă plata necesită actualizare, contactează suportul pentru reluarea lui."));
        }

        FleetOnboarding progress = user.FleetOnboarding;
        if (progress.CheckoutClientSecret is not null)
        {
            string sessionId = progress.CheckoutClientSecret.Split("_secret_", StringSplitOptions.None)[0];
            (string status, _) = await stripe.GetSessionStatusAsync(sessionId, ct);
            if (status == "open")
            {
                return progress.CheckoutClientSecret;
            }

            if (status == "complete")
            {
                return Result.Failure<string>(Error.Conflict("Fleet.PaymentPending", "Plata a fost trimisă. Așteptăm confirmarea Stripe."));
            }

            progress.CheckoutClientSecret = null;
            progress.CheckoutAttemptId = null;
        }
        progress.CheckoutAttemptId ??= Guid.NewGuid();
        await context.SaveChangesAsync(ct);
        bool eligible = progress.BcrEligibleAtUtc is not null && progress.CompletedAtUtc is null;
        StripeCatalogItem item = progress.Cycle == SubscriptionBillingCycle.Annual ? StripeCatalog.FleetAnnual : StripeCatalog.Fleet;
        string price = await stripe.ResolvePriceIdAsync(item, ct);
        string? coupon = eligible ? await stripe.EnsureAdvanceCreditCouponAsync(FleetPricing.BcrCoupon(progress.Cycle), ct) : null;
        string baseUrl = configuration["App:BaseUrl"] ?? throw new InvalidOperationException("App:BaseUrl missing.");
        string secret = await stripe.CreateCheckoutSessionAsync(price, "subscription",
            $"{baseUrl}/onboarding-srl?payment=returned", $"{baseUrl}/onboarding-srl",
            user.Email, user.Id.ToString(), $"plan:fleet|cycle:{progress.Cycle}{(progress.BcrRequested ? "|bcr:1" : string.Empty)}",
            new Dictionary<string, string> { ["fleetOnboarding"] = "true", ["fleetBcrApplied"] = eligible ? "true" : "false" },
            $"fleet:{user.Id}:{progress.CheckoutAttemptId}", coupon, ct);
        progress.CheckoutClientSecret = secret;
        await context.SaveChangesAsync(ct);
        return secret;
    }

    private Task<User?> UserAsync(CancellationToken ct) =>
        context.Users.SingleOrDefaultAsync(u => u.Id == userContext.UserId && u.Role == UserRole.CarPoster, ct);

    private Task<bool> BankConnectedAsync(Guid userId, CancellationToken ct) =>
        context.BankConnections.AnyAsync(b => b.UserId == userId && b.Status == Domain.Banking.BankConnectionStatus.Linked, ct);

    private async Task<bool> OblioConnectedAsync(User user, CancellationToken ct)
    {
        string? cif = await context.OblioIntegrations.Where(o => o.UserId == user.Id && o.IsConnected).Select(o => o.Cif).FirstOrDefaultAsync(ct);
        string normalized = new((cif ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return normalized == user.FleetOnboarding.Company?.Cui;
    }

    private async Task<FleetOnboardingResponse> ResponseAsync(User user, CancellationToken ct)
    {
        bool active = await context.UserSubscriptions.AnyAsync(s => s.UserId == user.Id && s.Plan == SubscriptionPlan.Fleet
            && s.Status == SubscriptionStatus.Active, ct);
        FleetOnboarding p = user.FleetOnboarding;
        return new(p, !user.FleetOnboardingRequired || p.CompletedAtUtc is not null && active,
            user.Email, user.PhoneNumber, user.IsEmailVerified, user.IsPhoneVerified,
            user.FirstName, user.LastName, await BankConnectedAsync(user.Id, ct),
            p.Company is not null && await OblioConnectedAsync(user, ct),
            FleetPricing.AmountDue(p.Cycle, p.BcrEligibleAtUtc is not null && p.CompletedAtUtc is null),
            FleetPricing.AmountDue(p.Cycle, false),
            FleetPricing.AmountDue(SubscriptionBillingCycle.Monthly, p.BcrEligibleAtUtc is not null && p.CompletedAtUtc is null),
            FleetPricing.AmountDue(SubscriptionBillingCycle.Annual, p.BcrEligibleAtUtc is not null && p.CompletedAtUtc is null),
            !user.FleetOnboardingRequired,
            ContactVerificationRequired);
    }

    private async Task SaveCompanyAsync(User user, FleetCompany company, CancellationToken ct)
    {
        CompanyProfile? profile = await context.CompanyProfiles.SingleOrDefaultAsync(c => c.UserId == user.Id, ct);
        if (profile is null)
        {
            profile = new() { Id = Guid.NewGuid(), UserId = user.Id, OwnerType = OwnerType.Srl, Slug = $"firma-{user.Id:N}" };
            context.CompanyProfiles.Add(profile);
        }
        profile.LegalName = company.Name;
        profile.Cui = company.Cui;
        profile.RegCom = company.RegistrationNumber;
        profile.RegisteredOffice = company.Address;
        profile.Email = user.Email;
        profile.Phone = user.PhoneNumber;
        profile.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static Result<FleetOnboardingResponse> Failure(string message) =>
        Result.Failure<FleetOnboardingResponse>(Error.Unprocessable("Fleet.InvalidStep", message));
}

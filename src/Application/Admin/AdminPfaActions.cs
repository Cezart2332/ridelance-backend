using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Payments;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Admin;

public sealed record ChangeAdminPfaPlanCommand(
    Guid PfaRegistrationId,
    SubscriptionPlan Plan,
    string Effective) : ICommand<AdminPfaDetailResponse>;

public sealed record ApplyAdminPfaDiscountCommand(
    Guid PfaRegistrationId,
    string Type,
    decimal Value,
    string? Note) : ICommand<AdminPfaDetailResponse>;

public sealed record SuspendAdminPfaCommand(
    Guid PfaRegistrationId,
    string? Reason) : ICommand<AdminPfaDetailResponse>;

public sealed record ReactivateAdminPfaCommand(
    Guid PfaRegistrationId,
    string? Note) : ICommand<AdminPfaDetailResponse>;

public sealed record UpdateAdminPfaInternalNoteCommand(
    Guid PfaRegistrationId,
    string Content) : ICommand<AdminPfaDetailResponse>;

internal sealed class ChangeAdminPfaPlanCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<ChangeAdminPfaPlanCommand, AdminPfaDetailResponse>
{
    public async Task<Result<AdminPfaDetailResponse>> Handle(
        ChangeAdminPfaPlanCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfaResult = await AdminPfaActionHelpers.GetPfaForAdminAsync(
            context,
            userContext.UserId,
            command.PfaRegistrationId,
            cancellationToken);

        if (pfaResult.IsFailure)
        {
            return Result.Failure<AdminPfaDetailResponse>(pfaResult.Error);
        }

        UserSubscription? subscription = await context.UserSubscriptions
            .Where(s => s.UserId == pfaResult.Value.UserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            subscription = new UserSubscription
            {
                Id = Guid.NewGuid(),
                UserId = pfaResult.Value.UserId,
                Plan = command.Plan,
                Status = SubscriptionStatus.ActivePendingBilling,
                FirstBillingDateUtc = DateTime.UtcNow,
                NextBillingDateUtc = DateTime.UtcNow,
                DashboardAccessGranted = true,
                DashboardAccessGrantedUtc = DateTime.UtcNow
            };
            context.UserSubscriptions.Add(subscription);
        }
        else if (string.Equals(command.Effective, "immediate", StringComparison.OrdinalIgnoreCase))
        {
            subscription.Plan = command.Plan;
            subscription.PendingPlan = null;
        }
        else
        {
            subscription.PendingPlan = command.Plan;
        }

        await AdminPfaActionHelpers.AddLogAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            "PlanChanged",
            $"Plan schimbat la {AdminBillingLabels.PlanLabel(command.Plan)} ({(string.Equals(command.Effective, "immediate", StringComparison.OrdinalIgnoreCase) ? "imediat" : "următorul ciclu")}).",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return await AdminPfaActionHelpers.LoadDetailAsync(context, pfaResult.Value.Id, cancellationToken);
    }
}

internal sealed class ApplyAdminPfaDiscountCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<ApplyAdminPfaDiscountCommand, AdminPfaDetailResponse>
{
    public async Task<Result<AdminPfaDetailResponse>> Handle(
        ApplyAdminPfaDiscountCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfaResult = await AdminPfaActionHelpers.GetPfaForAdminAsync(
            context,
            userContext.UserId,
            command.PfaRegistrationId,
            cancellationToken);

        if (pfaResult.IsFailure)
        {
            return Result.Failure<AdminPfaDetailResponse>(pfaResult.Error);
        }

        if (command.Value <= 0)
        {
            return Result.Failure<AdminPfaDetailResponse>(
                Error.Problem("Discount.Invalid", "Valoarea discountului trebuie să fie mai mare decât zero."));
        }

        string description = command.Type.Equals("fixed", StringComparison.OrdinalIgnoreCase)
            ? $"Discount aplicat: {command.Value:0.##} lei."
            : $"Discount aplicat: {command.Value:0.##}%.";

        await AdminPfaActionHelpers.AddLogAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            "DiscountApplied",
            string.IsNullOrWhiteSpace(command.Note) ? description : $"{description} {command.Note.Trim()}",
            cancellationToken);

        await AdminPfaActionHelpers.UpsertCurrentNoteAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            string.IsNullOrWhiteSpace(command.Note) ? description : $"{description}\n{command.Note.Trim()}",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return await AdminPfaActionHelpers.LoadDetailAsync(context, pfaResult.Value.Id, cancellationToken);
    }
}

internal sealed class SuspendAdminPfaCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<SuspendAdminPfaCommand, AdminPfaDetailResponse>
{
    public async Task<Result<AdminPfaDetailResponse>> Handle(
        SuspendAdminPfaCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfaResult = await AdminPfaActionHelpers.GetPfaForAdminAsync(
            context,
            userContext.UserId,
            command.PfaRegistrationId,
            cancellationToken);

        if (pfaResult.IsFailure)
        {
            return Result.Failure<AdminPfaDetailResponse>(pfaResult.Error);
        }

        UserSubscription? subscription = await context.UserSubscriptions
            .Where(s => s.UserId == pfaResult.Value.UserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is UserSubscription existingSubscription)
        {
            existingSubscription.Status = SubscriptionStatus.Expired;
        }

        await AdminPfaActionHelpers.AddLogAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            "AccountSuspended",
            string.IsNullOrWhiteSpace(command.Reason)
                ? "Cont suspendat."
                : $"Cont suspendat. Motiv: {command.Reason.Trim()}",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return await AdminPfaActionHelpers.LoadDetailAsync(context, pfaResult.Value.Id, cancellationToken);
    }
}

internal sealed class ReactivateAdminPfaCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<ReactivateAdminPfaCommand, AdminPfaDetailResponse>
{
    public async Task<Result<AdminPfaDetailResponse>> Handle(
        ReactivateAdminPfaCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfaResult = await AdminPfaActionHelpers.GetPfaForAdminAsync(
            context,
            userContext.UserId,
            command.PfaRegistrationId,
            cancellationToken);

        if (pfaResult.IsFailure)
        {
            return Result.Failure<AdminPfaDetailResponse>(pfaResult.Error);
        }

        UserSubscription? subscription = await context.UserSubscriptions
            .Where(s => s.UserId == pfaResult.Value.UserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is UserSubscription existingSubscription)
        {
            existingSubscription.Status = SubscriptionStatus.Active;
        }

        await AdminPfaActionHelpers.AddLogAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            "AccountReactivated",
            string.IsNullOrWhiteSpace(command.Note)
                ? "Cont reactivat."
                : $"Cont reactivat. {command.Note.Trim()}",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return await AdminPfaActionHelpers.LoadDetailAsync(context, pfaResult.Value.Id, cancellationToken);
    }
}

/// <summary>
/// BCR a confirmat contul clientului: de aici pornesc cele șase luni de reducere.
///
/// E o acțiune de om, nu un automatism, pentru că informația vine din afara sistemului — cineva
/// de la BCR spune că s-a deschis contul. Bifa de la checkout e doar intenția clientului.
/// </summary>
public sealed record ConfirmAdminPfaBcrDiscountCommand(
    Guid PfaRegistrationId,
    string? Note) : ICommand<AdminPfaDetailResponse>;

internal sealed class ConfirmAdminPfaBcrDiscountCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IStripeService stripeService)
    : ICommandHandler<ConfirmAdminPfaBcrDiscountCommand, AdminPfaDetailResponse>
{
    public async Task<Result<AdminPfaDetailResponse>> Handle(
        ConfirmAdminPfaBcrDiscountCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfaResult = await AdminPfaActionHelpers.GetPfaForAdminAsync(
            context,
            userContext.UserId,
            command.PfaRegistrationId,
            cancellationToken);

        if (pfaResult.IsFailure)
        {
            return Result.Failure<AdminPfaDetailResponse>(pfaResult.Error);
        }

        UserSubscription? subscription = await context.UserSubscriptions
            .Where(s => s.UserId == pfaResult.Value.UserId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            return Result.Failure<AdminPfaDetailResponse>(
                Error.Problem("BcrDiscount.NoSubscription", "Clientul nu are un abonament pe care să se aplice reducerea."));
        }

        if (subscription.BcrDiscountConfirmedAtUtc is not null)
        {
            // A doua confirmare ar reporni cele șase luni de la zero. Reducerea e una singură.
            return Result.Failure<AdminPfaDetailResponse>(
                Error.Problem("BcrDiscount.AlreadyConfirmed", "Reducerea BCR e deja confirmată pentru acest client."));
        }

        if (string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            return Result.Failure<AdminPfaDetailResponse>(
                Error.Problem("BcrDiscount.NoStripeSubscription", "Abonamentul nu are corespondent în Stripe, deci reducerea nu poate fi aplicată."));
        }

        // Stripe înainte de baza noastră de date: dacă apelul eșuează, nu rămânem cu un abonament
        // marcat „redus" pe care Stripe îl facturează întreg.
        await stripeService.ApplyBcrDiscountAsync(subscription.StripeSubscriptionId, cancellationToken);

        subscription.BcrDiscountConfirmedAtUtc = DateTime.UtcNow;

        string description =
            $"Reducere BCR confirmată: {Pricing.BcrDiscount.MonthlyBani / 100m:0.##} lei pe lună, {Pricing.BcrDiscount.Months} luni.";

        await AdminPfaActionHelpers.AddLogAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            "BcrDiscountConfirmed",
            string.IsNullOrWhiteSpace(command.Note) ? description : $"{description} {command.Note.Trim()}",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return await AdminPfaActionHelpers.LoadDetailAsync(context, pfaResult.Value.Id, cancellationToken);
    }
}

/// <summary>
/// RL-05 — numele nu se mai cere la înregistrare, iar OCR-ul îl completează abia după uploadul
/// buletinului. Adminul are nevoie de o cale manuală pentru conturile care rămân fără el.
/// </summary>
public sealed record UpdateAdminPfaClientNameCommand(
    Guid PfaRegistrationId,
    string? FirstName,
    string? LastName) : ICommand<AdminPfaDetailResponse>;

internal sealed class UpdateAdminPfaClientNameCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpdateAdminPfaClientNameCommand, AdminPfaDetailResponse>
{
    public async Task<Result<AdminPfaDetailResponse>> Handle(
        UpdateAdminPfaClientNameCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfaResult = await AdminPfaActionHelpers.GetPfaForAdminAsync(
            context,
            userContext.UserId,
            command.PfaRegistrationId,
            cancellationToken);

        if (pfaResult.IsFailure)
        {
            return Result.Failure<AdminPfaDetailResponse>(pfaResult.Error);
        }

        User? client = await context.Users
            .SingleOrDefaultAsync(u => u.Id == pfaResult.Value.UserId, cancellationToken);

        if (client is null)
        {
            return Result.Failure<AdminPfaDetailResponse>(UserErrors.NotFound(pfaResult.Value.UserId));
        }

        client.FirstName = command.FirstName?.Trim() ?? string.Empty;
        client.LastName = command.LastName?.Trim() ?? string.Empty;

        await AdminPfaActionHelpers.AddLogAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            "ClientNameUpdated",
            $"Nume client actualizat manual: {UserDisplayName.Of(client)}.",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return await AdminPfaActionHelpers.LoadDetailAsync(context, pfaResult.Value.Id, cancellationToken);
    }
}

internal sealed class UpdateAdminPfaInternalNoteCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext)
    : ICommandHandler<UpdateAdminPfaInternalNoteCommand, AdminPfaDetailResponse>
{
    public async Task<Result<AdminPfaDetailResponse>> Handle(
        UpdateAdminPfaInternalNoteCommand command,
        CancellationToken cancellationToken)
    {
        Result<PfaRegistration> pfaResult = await AdminPfaActionHelpers.GetPfaForAdminAsync(
            context,
            userContext.UserId,
            command.PfaRegistrationId,
            cancellationToken);

        if (pfaResult.IsFailure)
        {
            return Result.Failure<AdminPfaDetailResponse>(pfaResult.Error);
        }

        await AdminPfaActionHelpers.UpsertCurrentNoteAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            command.Content,
            cancellationToken);

        await AdminPfaActionHelpers.AddLogAsync(
            context,
            pfaResult.Value.Id,
            userContext.UserId,
            "InternalNoteUpdated",
            "Note interne actualizate.",
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        return await AdminPfaActionHelpers.LoadDetailAsync(context, pfaResult.Value.Id, cancellationToken);
    }
}

internal static class AdminPfaActionHelpers
{
    public static async Task<Result<PfaRegistration>> GetPfaForAdminAsync(
        IApplicationDbContext context,
        Guid userId,
        Guid pfaRegistrationId,
        CancellationToken cancellationToken)
    {
        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (caller is null || caller.Role != UserRole.Admin)
        {
            return Result.Failure<PfaRegistration>(
                Error.Failure("Admin.Forbidden", "Doar administratorii pot modifica aceste date."));
        }

        PfaRegistration? pfa = await context.PfaRegistrations
            .Include(p => p.User)
            .Include(p => p.Documents)
            .SingleOrDefaultAsync(p => p.Id == pfaRegistrationId, cancellationToken);

        return pfa is null
            ? Result.Failure<PfaRegistration>(Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."))
            : pfa;
    }

    public static Task AddLogAsync(
        IApplicationDbContext context,
        Guid pfaRegistrationId,
        Guid userId,
        string activityType,
        string description,
        CancellationToken cancellationToken)
    {
        context.PfaActivityLogs.Add(new PfaActivityLog
        {
            Id = Guid.NewGuid(),
            PfaRegistrationId = pfaRegistrationId,
            ActivityType = activityType,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow,
            PerformedByUserId = userId
        });

        return Task.CompletedTask;
    }

    public static async Task UpsertCurrentNoteAsync(
        IApplicationDbContext context,
        Guid pfaRegistrationId,
        Guid userId,
        string content,
        CancellationToken cancellationToken)
    {
        int year = DateTime.UtcNow.Year;
        int month = DateTime.UtcNow.Month;
        string normalizedContent = content.Trim();

        PfaInternalNote? note = await context.PfaInternalNotes
            .Where(n => n.PfaRegistrationId == pfaRegistrationId && n.Year == year && n.Month == month)
            .OrderByDescending(n => n.UpdatedAtUtc ?? n.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (note is null)
        {
            context.PfaInternalNotes.Add(new PfaInternalNote
            {
                Id = Guid.NewGuid(),
                PfaRegistrationId = pfaRegistrationId,
                Year = year,
                Month = month,
                Content = normalizedContent,
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            note.Content = normalizedContent;
            note.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public static async Task<Result<AdminPfaDetailResponse>> LoadDetailAsync(
        IApplicationDbContext context,
        Guid pfaRegistrationId,
        CancellationToken cancellationToken)
    {
        var handler = new GetAdminPfaDetailQueryHandler(context);
        return await handler.Handle(new GetAdminPfaDetailQuery(pfaRegistrationId), cancellationToken);
    }
}

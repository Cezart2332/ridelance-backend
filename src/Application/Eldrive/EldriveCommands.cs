using System.Net.Mail;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Abstractions.Services;
using Domain.Eldrive;
using Domain.Payments;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Eldrive;

/// <summary>Invitația activă a clientului, cum o arată pagina de conexiune.</summary>
public sealed record EldriveConnectionDto(bool Connected, string? Email, string? Status, DateTime? ConnectedAtUtc);

/// <summary>Clientul își conectează contul Eldrive: RIDElance îl invită în contul de partener.</summary>
public sealed record ConnectEldriveCommand(string Email) : ICommand<EldriveConnectionDto>;

public sealed record GetEldriveConnectionQuery : IQuery<EldriveConnectionDto>;

/// <summary>Un rând din evidența adminului.</summary>
/// <param name="SubscriptionActive">
/// Abonamentul clientului e plătit. Fals pe o invitație activă înseamnă că Eldrive trebuie scos.
/// </param>
public sealed record EldriveInviteAdminDto(
    Guid Id,
    Guid UserId,
    string ClientName,
    string AccountEmail,
    string EldriveEmail,
    long EldriveInviteId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? RemovedAtUtc,
    string? SubscriptionStatus,
    bool SubscriptionActive);

public sealed record GetEldriveInvitesQuery : IQuery<List<EldriveInviteAdminDto>>;

/// <summary>Adminul scoate invitația, la Eldrive și în evidență.</summary>
public sealed record RemoveEldriveInviteCommand(Guid InviteId) : ICommand;

internal static class EldriveErrors
{
    public static readonly Error InvalidEmail = Error.Problem("Eldrive.InvalidEmail", "Adresa de email nu e validă.");

    public static readonly Error NoSubscription = Error.Problem(
        "Eldrive.NoSubscription", "Eldrive e inclus în abonamentul RIDElance. Activează un abonament ca să te conectezi.");

    public static readonly Error AlreadyConnected = Error.Conflict("Eldrive.AlreadyConnected", "Contul Eldrive e deja conectat.");

    public static readonly Error NotFound = Error.NotFound("Eldrive.InviteNotFound", "Invitația Eldrive nu există.");

    public static bool IsActive(SubscriptionStatus? status) =>
        status is SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling;
}

internal sealed class GetEldriveConnectionQueryHandler(IApplicationDbContext context, IUserContext userContext)
    : IQueryHandler<GetEldriveConnectionQuery, EldriveConnectionDto>
{
    public async Task<Result<EldriveConnectionDto>> Handle(GetEldriveConnectionQuery query, CancellationToken cancellationToken)
    {
        EldriveInvite? invite = await context.EldriveInvites.AsNoTracking()
            .SingleOrDefaultAsync(i => i.UserId == userContext.UserId && i.RemovedAtUtc == null, cancellationToken);

        return invite is null
            ? new EldriveConnectionDto(false, null, null, null)
            : new EldriveConnectionDto(true, invite.Email, invite.Status, invite.CreatedAtUtc);
    }
}

internal sealed class ConnectEldriveCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IEldriveService eldrive)
    : ICommandHandler<ConnectEldriveCommand, EldriveConnectionDto>
{
    public async Task<Result<EldriveConnectionDto>> Handle(ConnectEldriveCommand command, CancellationToken cancellationToken)
    {
        string email = command.Email?.Trim() ?? string.Empty;
        if (email.Length is 0 or > 256 || !MailAddress.TryCreate(email, out MailAddress? parsed) || parsed.Address != email)
        {
            return Result.Failure<EldriveConnectionDto>(EldriveErrors.InvalidEmail);
        }

        Guid userId = userContext.UserId;

        if (await context.EldriveInvites.AnyAsync(i => i.UserId == userId && i.RemovedAtUtc == null, cancellationToken))
        {
            return Result.Failure<EldriveConnectionDto>(EldriveErrors.AlreadyConnected);
        }

        SubscriptionStatus? subscription = await context.UserSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => (SubscriptionStatus?)s.Status)
            .FirstOrDefaultAsync(cancellationToken);

        if (!EldriveErrors.IsActive(subscription))
        {
            return Result.Failure<EldriveConnectionDto>(EldriveErrors.NoSubscription);
        }

        Result<EldriveInviteResult> invited = await eldrive.InviteAsync(email, cancellationToken);
        if (invited.IsFailure)
        {
            return Result.Failure<EldriveConnectionDto>(invited.Error);
        }

        var invite = new EldriveInvite
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Email = email,
            EldriveInviteId = invited.Value.InviteId,
            EldriveUserId = invited.Value.EldriveUserId,
            Status = invited.Value.Status,
            CreatedAtUtc = DateTime.UtcNow,
        };
        context.EldriveInvites.Add(invite);
        await context.SaveChangesAsync(cancellationToken);

        return new EldriveConnectionDto(true, invite.Email, invite.Status, invite.CreatedAtUtc);
    }
}

internal sealed class GetEldriveInvitesQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetEldriveInvitesQuery, List<EldriveInviteAdminDto>>
{
    public async Task<Result<List<EldriveInviteAdminDto>>> Handle(GetEldriveInvitesQuery query, CancellationToken cancellationToken)
    {
        var invites = await context.EldriveInvites.AsNoTracking()
            .Select(i => new
            {
                i.Id,
                i.UserId,
                i.User.FirstName,
                i.User.LastName,
                AccountEmail = i.User.Email,
                i.Email,
                i.EldriveInviteId,
                i.Status,
                i.CreatedAtUtc,
                i.RemovedAtUtc,
            })
            .ToListAsync(cancellationToken);

        List<Guid> userIds = [.. invites.Select(i => i.UserId).Distinct()];
        var subscriptions = (await context.UserSubscriptions.AsNoTracking()
                .Where(s => userIds.Contains(s.UserId))
                .Select(s => new { s.UserId, s.Status, s.CreatedAtUtc })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CreatedAtUtc).First().Status);

        // Întâi invitațiile active fără abonament — pe ele trebuie acționat —, apoi restul active,
        // apoi istoricul.
        return invites
            .Select(i =>
            {
                SubscriptionStatus? status = subscriptions.TryGetValue(i.UserId, out SubscriptionStatus s) ? s : null;
                return new EldriveInviteAdminDto(
                    i.Id,
                    i.UserId,
                    $"{i.FirstName} {i.LastName}".Trim(),
                    i.AccountEmail,
                    i.Email,
                    i.EldriveInviteId,
                    i.Status,
                    i.CreatedAtUtc,
                    i.RemovedAtUtc,
                    status?.ToString(),
                    EldriveErrors.IsActive(status));
            })
            .OrderBy(i => i.RemovedAtUtc is not null)
            .ThenBy(i => i.SubscriptionActive)
            .ThenByDescending(i => i.CreatedAtUtc)
            .ToList();
    }
}

internal sealed class RemoveEldriveInviteCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IEldriveService eldrive)
    : ICommandHandler<RemoveEldriveInviteCommand>
{
    public async Task<Result> Handle(RemoveEldriveInviteCommand command, CancellationToken cancellationToken)
    {
        EldriveInvite? invite = await context.EldriveInvites
            .SingleOrDefaultAsync(i => i.Id == command.InviteId, cancellationToken);

        if (invite is null)
        {
            return Result.Failure(EldriveErrors.NotFound);
        }

        if (invite.RemovedAtUtc is not null)
        {
            return Result.Success();
        }

        // Întâi la Eldrive: dacă acolo nu merge, evidența rămâne „activă”, adică adevărată.
        Result removed = await eldrive.DeleteInviteAsync(invite.EldriveInviteId, cancellationToken);
        if (removed.IsFailure)
        {
            return removed;
        }

        invite.RemovedAtUtc = DateTime.UtcNow;
        invite.RemovedByUserId = userContext.UserId;
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

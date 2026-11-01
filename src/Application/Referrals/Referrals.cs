using System.Security.Cryptography;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Payments;
using Domain.Referrals;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

namespace Application.Referrals;

/// <summary>Un prieten invitat sau înscris cu codul clientului, cu stadiul lui de acum.</summary>
/// <param name="Stage">
/// <c>Invited</c> (fără cont), <c>Onboarding</c>, <c>Active</c> (abonament activ), <c>Inactive</c>
/// (înrolat, fără abonament activ) sau <c>Closed</c> (cont închis).
/// </param>
public sealed record ReferralDto(
    string Email,
    string? Name,
    DateTime? InvitedAtUtc,
    DateTime? JoinedAtUtc,
    bool Accepted,
    string Stage);

public sealed record ReferralsResponse(string Code, string Link, IReadOnlyList<ReferralDto> Referrals);

/// <summary>Codul și invitațiile clientului autentificat.</summary>
public sealed record GetMyReferralsQuery : IQuery<ReferralsResponse>;

/// <summary>Codul și invitațiile unui client, pentru admin.</summary>
public sealed record GetClientReferralsQuery(Guid UserId) : IQuery<ReferralsResponse>;

/// <summary>Trimite invitația pe email; a doua invitație către aceeași adresă o retrimite.</summary>
public sealed record InviteReferralCommand(string Email) : ICommand<ReferralsResponse>;

public static class ReferralErrors
{
    public static readonly Error NotAClient = Error.Failure(
        "Referrals.NotAClient", "Doar clienții au cod de recomandare.");

    public static readonly Error InvalidEmail = Error.Problem(
        "Referrals.InvalidEmail", "Adresa de email nu pare validă.");

    public static readonly Error OwnEmail = Error.Problem(
        "Referrals.OwnEmail", "Nu te poți invita pe tine.");

    public static readonly Error AlreadyRegistered = Error.Conflict(
        "Referrals.AlreadyRegistered", "Există deja un cont RIDElance cu adresa asta.");

    public static readonly Error SentRecently = Error.Problem(
        "Referrals.SentRecently", "Invitația a plecat deja azi către adresa asta.");
}

public static class ReferralCodes
{
    /// <summary>Fără 0/O și 1/I/L, care se confundă când codul e dictat sau copiat de pe ecran.</summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 8;

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>Forma în care se caută: fără spații, cu majuscule.</summary>
    public static string? Normalize(string? code)
    {
        string? value = code?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(value) || value.Length > 16 ? null : value;
    }

    /// <summary>Codul contului, generat acum dacă nu are. Nu salvează: apelantul face SaveChanges.</summary>
    public static async Task<string> EnsureAsync(IApplicationDbContext context, User user, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(user.ReferralCode))
        {
            return user.ReferralCode;
        }

        string code;
        do
        {
            code = New();
        }
        while (await context.Users.AnyAsync(u => u.ReferralCode == code, cancellationToken));

        user.ReferralCode = code;
        return code;
    }
}

internal static class ReferralHistory
{
    public static bool IsClient(User user) => user.Role is UserRole.Client or UserRole.CarPoster;

    public static string Link(IConfiguration configuration, string code) =>
        $"{(configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/')}/inregistrare?ref={Uri.EscapeDataString(code)}";

    /// <summary>
    /// Invitațiile trimise și conturile create cu codul, într-o singură listă: un prieten invitat
    /// pe email și înscris apoi cu codul apare o dată. Cele mai noi primele.
    /// </summary>
    public static async Task<ReferralsResponse> LoadAsync(
        IApplicationDbContext context,
        IConfiguration configuration,
        User inviter,
        CancellationToken cancellationToken)
    {
        string code = await ReferralCodes.EnsureAsync(context, inviter, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        List<ReferralInvitation> invitations = await context.ReferralInvitations
            .AsNoTracking()
            .Where(i => i.InviterUserId == inviter.Id)
            .ToListAsync(cancellationToken);

        string[] invitedEmails = invitations.Select(i => i.Email).ToArray();

        var accounts = await context.Users
            .AsNoTracking()
            .Where(u => u.ReferredByUserId == inviter.Id || invitedEmails.Contains(u.Email))
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.Role,
                u.CreatedAtUtc,
                u.DeletedAtUtc,
                OnboardingCompletedAtUtc = context.PfaRegistrations
                    .Where(r => r.UserId == u.Id)
                    .Select(r => r.OnboardingCompletedAtUtc)
                    .FirstOrDefault(),
                Subscription = context.UserSubscriptions
                    .Where(s => s.UserId == u.Id)
                    .OrderByDescending(s => s.CreatedAtUtc)
                    .Select(s => (SubscriptionStatus?)s.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var byEmail = accounts
            .GroupBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<ReferralDto>();

        foreach (ReferralInvitation invitation in invitations)
        {
            if (byEmail.Remove(invitation.Email, out var account))
            {
                rows.Add(new ReferralDto(
                    account.Email,
                    Name(account.FirstName, account.LastName),
                    invitation.CreatedAtUtc,
                    account.CreatedAtUtc,
                    true,
                    Stage(account.DeletedAtUtc, account.Role, account.OnboardingCompletedAtUtc, account.Subscription)));
            }
            else
            {
                rows.Add(new ReferralDto(invitation.Email, null, invitation.CreatedAtUtc, null, false, "Invited"));
            }
        }

        // Înscriși cu codul, fără invitație pe email (linkul trimis pe WhatsApp, codul dictat).
        rows.AddRange(byEmail.Values.Select(account => new ReferralDto(
            account.Email,
            Name(account.FirstName, account.LastName),
            null,
            account.CreatedAtUtc,
            true,
            Stage(account.DeletedAtUtc, account.Role, account.OnboardingCompletedAtUtc, account.Subscription))));

        return new ReferralsResponse(
            code,
            Link(configuration, code),
            rows.OrderByDescending(r => r.JoinedAtUtc ?? r.InvitedAtUtc).ToList());
    }

    internal static string Stage(
        DateTime? deletedAtUtc,
        UserRole role,
        DateTime? onboardingCompletedAtUtc,
        SubscriptionStatus? subscription)
    {
        if (deletedAtUtc is not null)
        {
            return "Closed";
        }

        if (subscription is SubscriptionStatus.Active or SubscriptionStatus.ActivePendingBilling)
        {
            return "Active";
        }

        // Firma (SRL) n-are dosar PFA: până la primul abonament e în configurare.
        bool enrolled = role == UserRole.Client ? onboardingCompletedAtUtc is not null : subscription is not null;
        return enrolled ? "Inactive" : "Onboarding";
    }

    private static string? Name(string firstName, string lastName)
    {
        string name = $"{firstName} {lastName}".Trim();
        return name.Length == 0 ? null : name;
    }
}

internal sealed class GetMyReferralsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IConfiguration configuration)
    : IQueryHandler<GetMyReferralsQuery, ReferralsResponse>
{
    public async Task<Result<ReferralsResponse>> Handle(GetMyReferralsQuery query, CancellationToken cancellationToken)
    {
        User? user = await context.Users.SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);
        if (user is null || !ReferralHistory.IsClient(user))
        {
            return Result.Failure<ReferralsResponse>(ReferralErrors.NotAClient);
        }

        return await ReferralHistory.LoadAsync(context, configuration, user, cancellationToken);
    }
}

internal sealed class GetClientReferralsQueryHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IConfiguration configuration)
    : IQueryHandler<GetClientReferralsQuery, ReferralsResponse>
{
    public async Task<Result<ReferralsResponse>> Handle(GetClientReferralsQuery query, CancellationToken cancellationToken)
    {
        bool isAdmin = await context.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userContext.UserId && u.Role == UserRole.Admin && u.DeletedAtUtc == null, cancellationToken);
        if (!isAdmin)
        {
            return Result.Failure<ReferralsResponse>(Error.Failure("Admin.Forbidden", "Doar administratorii pot vedea recomandările clienților."));
        }

        User? client = await context.Users.SingleOrDefaultAsync(u => u.Id == query.UserId, cancellationToken);
        if (client is null || !ReferralHistory.IsClient(client))
        {
            return Result.Failure<ReferralsResponse>(UserErrors.NotFound(query.UserId));
        }

        return await ReferralHistory.LoadAsync(context, configuration, client, cancellationToken);
    }
}

internal sealed class InviteReferralCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    IConfiguration configuration,
    Abstractions.IEmailService emailService,
    Abstractions.IMjmlRenderer mjmlRenderer)
    : ICommandHandler<InviteReferralCommand, ReferralsResponse>
{
    public async Task<Result<ReferralsResponse>> Handle(InviteReferralCommand command, CancellationToken cancellationToken)
    {
        User? inviter = await context.Users.SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);
        if (inviter is null || !ReferralHistory.IsClient(inviter))
        {
            return Result.Failure<ReferralsResponse>(ReferralErrors.NotAClient);
        }

        string email = EmailAddress.Normalize(command.Email);
        if (!System.Net.Mail.MailAddress.TryCreate(email, out System.Net.Mail.MailAddress? parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.Ordinal) ||
            !email.Contains('.', StringComparison.Ordinal) ||
            email.Length > 256)
        {
            return Result.Failure<ReferralsResponse>(ReferralErrors.InvalidEmail);
        }

        if (string.Equals(email, inviter.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ReferralsResponse>(ReferralErrors.OwnEmail);
        }

        if (await context.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return Result.Failure<ReferralsResponse>(ReferralErrors.AlreadyRegistered);
        }

        DateTime now = DateTime.UtcNow;
        ReferralInvitation? invitation = await context.ReferralInvitations
            .SingleOrDefaultAsync(i => i.InviterUserId == inviter.Id && i.Email == email, cancellationToken);

        if (invitation is null)
        {
            invitation = new ReferralInvitation { Id = Guid.NewGuid(), InviterUserId = inviter.Id, Email = email, CreatedAtUtc = now };
            context.ReferralInvitations.Add(invitation);
        }
        else if (invitation.LastSentAtUtc > now.AddHours(-24))
        {
            return Result.Failure<ReferralsResponse>(ReferralErrors.SentRecently);
        }

        invitation.LastSentAtUtc = now;

        string code = await ReferralCodes.EnsureAsync(context, inviter, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        Result sent = await ReferralInvitationEmail.SendAsync(
            emailService,
            mjmlRenderer,
            email,
            UserDisplayName.Of(inviter.FirstName, inviter.LastName, inviter.Email),
            code,
            ReferralHistory.Link(configuration, code),
            cancellationToken);

        if (sent.IsFailure)
        {
            return Result.Failure<ReferralsResponse>(sent.Error);
        }

        return await ReferralHistory.LoadAsync(context, configuration, inviter, cancellationToken);
    }
}

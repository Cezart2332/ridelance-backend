using Application.Abstractions;
using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Application.Users.Login;
using Application.Users.TwoFactor;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SharedKernel;

// Emailurile se compară după `lower()` în SQL (EF traduce `ToLower`); adresele sunt ASCII.
#pragma warning disable CA1304, CA1311, CA1862, CA1308

namespace Application.Users.Staff;

public sealed record StaffMemberDto(
    Guid Id, string Name, string Email, string Role, bool IsOwner, bool TwoFactorEnabled, DateTime CreatedAtUtc, DateTime? LastActivityAtUtc, bool Closed);

public sealed record StaffInvitationDto(Guid Id, string FullName, string Email, string Role, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);

public sealed record StaffOverviewDto(IReadOnlyList<StaffMemberDto> Members, IReadOnlyList<StaffInvitationDto> Invitations, bool CanInviteAdmins);

/// <summary>Ce vede cel invitat înainte să-și creeze contul.</summary>
public sealed record StaffInvitationPreviewDto(string FullName, string Email, string Role);

public static class StaffErrors
{
    public static readonly Error NotAdmin = Error.Failure("Staff.NotAdmin", "Doar un administrator poate gestiona echipa.");
    public static readonly Error OwnerOnly = Error.Failure("Staff.OwnerOnly", "Doar proprietarul contului poate invita sau gestiona administratori.");
    public static readonly Error InvalidRole = Error.Problem("Staff.InvalidRole", "Rolul trebuie să fie Admin sau Contabil.");
    public static readonly Error EmailInUse = Error.Conflict("Staff.EmailInUse", "Există deja un cont cu acest email.");
    public static readonly Error InvalidInput = Error.Problem("Staff.InvalidInput", "Completează numele și un email valid.");
    public static readonly Error InvitationInvalid = Error.NotFound("Staff.InvitationInvalid", "Invitația nu mai este valabilă. Cere una nouă.");
    public static readonly Error PasswordTooShort = Error.Problem("Staff.PasswordTooShort", "Parola trebuie să aibă cel puțin 10 caractere.");
    public static readonly Error NotStaff = Error.NotFound("Staff.NotStaff", "Contul nu face parte din echipă.");
    public static readonly Error CannotResetSelf = Error.Problem("Staff.CannotResetSelf", "Nu îți poți reseta singur autentificarea în doi pași.");
}

internal static class StaffSupport
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(3);
    public static readonly TimeSpan BootstrapLifetime = TimeSpan.FromHours(24);
    public const int MinPasswordLength = 10;

    public static async Task<Result<User>> AdminAsync(IApplicationDbContext db, IUserContext userContext, CancellationToken cancellationToken)
    {
        User? caller = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);
        return caller is { Role: UserRole.Admin, IsDeleted: false } ? caller : Result.Failure<User>(StaffErrors.NotAdmin);
    }

    public static string InvitationLink(IConfiguration configuration, string token)
    {
        string baseUrl = (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
        return $"{baseUrl}/invitatie/{token}";
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>O invitație nouă pentru adresă; cele deschise înainte pentru aceeași adresă se revocă.</summary>
    public static async Task<(StaffInvitation Invitation, string Token)> CreateAsync(
        IApplicationDbContext db, string fullName, string email, UserRole role, Guid? invitedBy, bool bootstrap, DateTime now, CancellationToken cancellationToken)
    {
        List<StaffInvitation> open = await db.StaffInvitations
            .Where(i => i.Email == email && i.AcceptedAtUtc == null && i.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (StaffInvitation previous in open)
        {
            previous.RevokedAtUtc = now;
        }

        string token = TwoFactorSupport.RandomToken();
        var invitation = new StaffInvitation
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName.Trim(),
            Role = role,
            TokenHash = TwoFactorSupport.Hash(token),
            ExpiresAtUtc = now + (bootstrap ? BootstrapLifetime : InvitationLifetime),
            IsBootstrap = bootstrap,
            InvitedByUserId = invitedBy,
            CreatedAtUtc = now,
        };
        db.StaffInvitations.Add(invitation);
        return (invitation, token);
    }

    public static async Task<StaffInvitation?> OpenByTokenAsync(IApplicationDbContext db, string? token, DateTime now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string hash = TwoFactorSupport.Hash(token);
        StaffInvitation? invitation = await db.StaffInvitations.SingleOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        return invitation is not null && invitation.IsOpen(now) ? invitation : null;
    }
}

/// <summary><c>POST /admin/staff/invitations</c> — invită un contabil sau (doar proprietarul) un admin.</summary>
public sealed record InviteStaffCommand(string FullName, string Email, string Role) : ICommand<StaffInvitationDto>;

internal sealed class InviteStaffCommandHandler(
    IApplicationDbContext db,
    IUserContext userContext,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    IConfiguration configuration,
    IDateTimeProvider clock) : ICommandHandler<InviteStaffCommand, StaffInvitationDto>
{
    public async Task<Result<StaffInvitationDto>> Handle(InviteStaffCommand command, CancellationToken cancellationToken)
    {
        Result<User> caller = await StaffSupport.AdminAsync(db, userContext, cancellationToken);
        if (caller.IsFailure)
        {
            return Result.Failure<StaffInvitationDto>(caller.Error);
        }

        if (!Enum.TryParse(command.Role, ignoreCase: true, out UserRole role) || role is not (UserRole.Admin or UserRole.Contabil))
        {
            return Result.Failure<StaffInvitationDto>(StaffErrors.InvalidRole);
        }

        if (role == UserRole.Admin && !caller.Value.IsOwner)
        {
            return Result.Failure<StaffInvitationDto>(StaffErrors.OwnerOnly);
        }

        string email = StaffSupport.NormalizeEmail(command.Email ?? string.Empty);
        if (string.IsNullOrWhiteSpace(command.FullName) || !email.Contains('@', StringComparison.Ordinal) || email.Length > 256)
        {
            return Result.Failure<StaffInvitationDto>(StaffErrors.InvalidInput);
        }

        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email, cancellationToken))
        {
            return Result.Failure<StaffInvitationDto>(StaffErrors.EmailInUse);
        }

        DateTime now = clock.UtcNow;
        (StaffInvitation invitation, string token) = await StaffSupport.CreateAsync(db, command.FullName, email, role, caller.Value.Id, false, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await StaffInvitationEmail.SendAsync(emailService, mjmlRenderer, invitation, StaffSupport.InvitationLink(configuration, token), cancellationToken);
        return new StaffInvitationDto(invitation.Id, invitation.FullName, invitation.Email, invitation.Role.ToString(), invitation.CreatedAtUtc, invitation.ExpiresAtUtc);
    }
}

/// <summary><c>GET /admin/staff</c> — echipa și invitațiile încă deschise.</summary>
public sealed record GetStaffQuery : IQuery<StaffOverviewDto>;

internal sealed class GetStaffQueryHandler(IApplicationDbContext db, IUserContext userContext, IDateTimeProvider clock) : IQueryHandler<GetStaffQuery, StaffOverviewDto>
{
    public async Task<Result<StaffOverviewDto>> Handle(GetStaffQuery query, CancellationToken cancellationToken)
    {
        Result<User> caller = await StaffSupport.AdminAsync(db, userContext, cancellationToken);
        if (caller.IsFailure)
        {
            return Result.Failure<StaffOverviewDto>(caller.Error);
        }

        DateTime now = clock.UtcNow;
        List<User> staff = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Admin || u.Role == UserRole.Contabil)
            .OrderByDescending(u => u.IsOwner).ThenBy(u => u.Role).ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);
        List<StaffInvitation> invitations = await db.StaffInvitations.AsNoTracking()
            .Where(i => i.AcceptedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now && !i.IsBootstrap)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new StaffOverviewDto(
            [.. staff.Select(u => new StaffMemberDto(
                u.Id, $"{u.FirstName} {u.LastName}".Trim(), u.Email, u.Role.ToString(), u.IsOwner, u.IsTwoFactorEnabled, u.CreatedAtUtc, u.LastActivityAtUtc, u.IsDeleted))],
            [.. invitations.Select(i => new StaffInvitationDto(i.Id, i.FullName, i.Email, i.Role.ToString(), i.CreatedAtUtc, i.ExpiresAtUtc))],
            caller.Value.IsOwner);
    }
}

/// <summary><c>POST /admin/staff/invitations/{id}/revoke</c>.</summary>
public sealed record RevokeStaffInvitationCommand(Guid InvitationId) : ICommand;

internal sealed class RevokeStaffInvitationCommandHandler(IApplicationDbContext db, IUserContext userContext, IDateTimeProvider clock) : ICommandHandler<RevokeStaffInvitationCommand>
{
    public async Task<Result> Handle(RevokeStaffInvitationCommand command, CancellationToken cancellationToken)
    {
        Result<User> caller = await StaffSupport.AdminAsync(db, userContext, cancellationToken);
        if (caller.IsFailure)
        {
            return caller;
        }

        StaffInvitation? invitation = await db.StaffInvitations.SingleOrDefaultAsync(i => i.Id == command.InvitationId, cancellationToken);
        if (invitation is null || !invitation.IsOpen(clock.UtcNow))
        {
            return Result.Failure(StaffErrors.InvitationInvalid);
        }

        if (invitation.Role == UserRole.Admin && !caller.Value.IsOwner)
        {
            return Result.Failure(StaffErrors.OwnerOnly);
        }

        invitation.RevokedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// <c>POST /admin/staff/{userId}/reset-2fa</c> — omul și-a pierdut telefonul: 2FA-ul se șterge și îl
/// configurează din nou la următorul login. Contabilii îi resetează orice admin; adminii, doar
/// proprietarul; proprietarului, nimeni (are codurile de recuperare și resetarea din configurare).
/// </summary>
public sealed record ResetStaffTwoFactorCommand(Guid UserId) : ICommand;

internal sealed class ResetStaffTwoFactorCommandHandler(IApplicationDbContext db, IUserContext userContext) : ICommandHandler<ResetStaffTwoFactorCommand>
{
    public async Task<Result> Handle(ResetStaffTwoFactorCommand command, CancellationToken cancellationToken)
    {
        Result<User> caller = await StaffSupport.AdminAsync(db, userContext, cancellationToken);
        if (caller.IsFailure)
        {
            return caller;
        }

        if (command.UserId == caller.Value.Id)
        {
            return Result.Failure(StaffErrors.CannotResetSelf);
        }

        User? target = await db.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (target is null || !target.IsStaff)
        {
            return Result.Failure(StaffErrors.NotStaff);
        }

        if (target.IsOwner || target.Role == UserRole.Admin && !caller.Value.IsOwner)
        {
            return Result.Failure(StaffErrors.OwnerOnly);
        }

        await TwoFactorSupport.ClearAsync(db, target, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary><c>GET /staff-invitations/{token}</c> (fără autentificare) — pentru pagina de creare a contului.</summary>
public sealed record GetStaffInvitationQuery(string Token) : IQuery<StaffInvitationPreviewDto>;

internal sealed class GetStaffInvitationQueryHandler(IApplicationDbContext db, IDateTimeProvider clock) : IQueryHandler<GetStaffInvitationQuery, StaffInvitationPreviewDto>
{
    public async Task<Result<StaffInvitationPreviewDto>> Handle(GetStaffInvitationQuery query, CancellationToken cancellationToken)
    {
        StaffInvitation? invitation = await StaffSupport.OpenByTokenAsync(db, query.Token, clock.UtcNow, cancellationToken);
        return invitation is null
            ? Result.Failure<StaffInvitationPreviewDto>(StaffErrors.InvitationInvalid)
            : new StaffInvitationPreviewDto(invitation.FullName, invitation.Email, invitation.Role.ToString());
    }
}

/// <summary>
/// <c>POST /staff-invitations/{token}/accept</c> — contul se creează cu parola aleasă; răspunsul e
/// pasul de configurare 2FA, fără sesiune: contul nu e utilizabil până nu-l termină.
/// </summary>
public sealed record AcceptStaffInvitationCommand(string Token, string Password) : ICommand<LoginResponse>;

internal sealed class AcceptStaffInvitationCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher, IDateTimeProvider clock)
    : ICommandHandler<AcceptStaffInvitationCommand, LoginResponse>
{
    public async Task<Result<LoginResponse>> Handle(AcceptStaffInvitationCommand command, CancellationToken cancellationToken)
    {
        DateTime now = clock.UtcNow;
        StaffInvitation? invitation = await StaffSupport.OpenByTokenAsync(db, command.Token, now, cancellationToken);
        if (invitation is null)
        {
            return Result.Failure<LoginResponse>(StaffErrors.InvitationInvalid);
        }

        if ((command.Password ?? string.Empty).Length < StaffSupport.MinPasswordLength)
        {
            return Result.Failure<LoginResponse>(StaffErrors.PasswordTooShort);
        }

        if (await db.Users.AnyAsync(u => u.Email.ToLower() == invitation.Email, cancellationToken))
        {
            return Result.Failure<LoginResponse>(StaffErrors.EmailInUse);
        }

        // Invitația de bootstrap e valabilă doar cât timp platforma n-are niciun admin.
        if (invitation.IsBootstrap && await db.Users.AnyAsync(u => u.Role == UserRole.Admin && u.DeletedAtUtc == null, cancellationToken))
        {
            invitation.RevokedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure<LoginResponse>(StaffErrors.InvitationInvalid);
        }

        string[] parts = invitation.FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = invitation.Email,
            FirstName = parts.Length > 0 ? parts[0] : invitation.FullName,
            LastName = parts.Length > 1 ? parts[1] : string.Empty,
            PasswordHash = passwordHasher.Hash(command.Password!),
            Role = invitation.Role,
            IsOwner = invitation.IsBootstrap,
            EmailVerifiedAtUtc = now,
            CreatedAtUtc = now,
        };
        db.Users.Add(user);
        invitation.AcceptedAtUtc = now;
        invitation.AcceptedUserId = user.Id;
        LoginResponse challenge = TwoFactorSupport.StartChallenge(user, now);
        await db.SaveChangesAsync(cancellationToken);
        return challenge;
    }
}

/// <summary>
/// La pornire: dacă platforma n-are niciun admin și <c>Bootstrap:AdminEmail</c> e setat, o invitație
/// de proprietar pentru adresa aceea (cele vechi se revocă). Întoarce linkul, pentru log, sau
/// <c>null</c> când nu e nimic de făcut. Cu un admin existent nu face nimic, oricât ar rămâne setată
/// variabila.
/// </summary>
public sealed record EnsureAdminBootstrapCommand(string? AdminEmail) : ICommand<string?>;

internal sealed class EnsureAdminBootstrapCommandHandler(
    IApplicationDbContext db,
    IEmailService emailService,
    IMjmlRenderer mjmlRenderer,
    IConfiguration configuration,
    IDateTimeProvider clock) : ICommandHandler<EnsureAdminBootstrapCommand, string?>
{
    public async Task<Result<string?>> Handle(EnsureAdminBootstrapCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.AdminEmail) ||
            await db.Users.AnyAsync(u => u.Role == UserRole.Admin && u.DeletedAtUtc == null, cancellationToken))
        {
            return Result.Success<string?>(null);
        }

        string email = StaffSupport.NormalizeEmail(command.AdminEmail);
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email, cancellationToken))
        {
            return Result.Failure<string?>(StaffErrors.EmailInUse);
        }

        (StaffInvitation invitation, string token) = await StaffSupport.CreateAsync(
            db, "Administrator RIDElance", email, UserRole.Admin, null, true, clock.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        string link = StaffSupport.InvitationLink(configuration, token);
        await StaffInvitationEmail.SendAsync(emailService, mjmlRenderer, invitation, link, cancellationToken);
        return link;
    }
}

/// <summary>
/// Ultima soluție când proprietarul și-a pierdut și telefonul, și codurile de recuperare:
/// <c>Security:ResetTwoFactorEmail</c> în configurare șterge 2FA-ul contului de echipă cu adresa aceea
/// la pornire. Variabila se scoate după folosire.
/// </summary>
public sealed record ResetTwoFactorFromConfigCommand(string? Email) : ICommand<bool>;

internal sealed class ResetTwoFactorFromConfigCommandHandler(IApplicationDbContext db) : ICommandHandler<ResetTwoFactorFromConfigCommand, bool>
{
    public async Task<Result<bool>> Handle(ResetTwoFactorFromConfigCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return false;
        }

        string email = StaffSupport.NormalizeEmail(command.Email);
        User? user = await db.Users.SingleOrDefaultAsync(u => u.Email.ToLower() == email, cancellationToken);
        if (user is null || !user.IsStaff || !user.IsTwoFactorEnabled)
        {
            return false;
        }

        await TwoFactorSupport.ClearAsync(db, user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

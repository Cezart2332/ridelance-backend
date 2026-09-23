using Application.Abstractions.Data;
using Domain.Chat;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Application.Chat;

/// <summary>
/// Cine are voie într-o cameră de chat: adminul în oricare, contabilul la clienții alocați lui
/// (sau unde e el profesionistul camerei), clientul doar în camera lui.
/// </summary>
internal static class ChatAccess
{
    public static async Task<bool> IsParticipantAsync(
        IApplicationDbContext context,
        ChatRoom room,
        User user,
        CancellationToken cancellationToken)
    {
        if (user.Role == UserRole.Admin)
        {
            return true;
        }

        if (user.Role == UserRole.Contabil)
        {
            if (room.ProfessionalUserId == user.Id)
            {
                return true;
            }

            Guid? assigned = await context.PfaRegistrations
                .AsNoTracking()
                .Where(p => p.UserId == room.ClientUserId)
                .Select(p => p.AssignedContabilId)
                .FirstOrDefaultAsync(cancellationToken);

            return assigned == user.Id;
        }

        return room.ClientUserId == user.Id;
    }
}

/// <summary>Numele afișat al expeditorului: echipa apare ca „Support Ridelance”.</summary>
public static class ChatSenderName
{
    public static string For(User sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        return sender.Role is UserRole.Admin or UserRole.Contabil
            ? "Support Ridelance"
            : $"{sender.FirstName} {sender.LastName}";
    }
}

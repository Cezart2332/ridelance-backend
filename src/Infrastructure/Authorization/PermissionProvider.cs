using Application.Abstractions.Data;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Authorization;

internal sealed class PermissionProvider(IApplicationDbContext context)
{
    public async Task<HashSet<string>> GetForUserIdAsync(Guid userId)
    {
        User? user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId);

        if (user is null)
        {
            return [];
        }

        // Map roles to permissions
        HashSet<string> permissions = user.Role switch
        {
            UserRole.Admin =>
            [
                Permissions.ViewPfaRegistrations,
                Permissions.ManagePfaRegistrations,
                Permissions.ManageContabili,
                Permissions.ViewAllDocuments,
                Permissions.DownloadDocuments,
                Permissions.ViewAllChats,
                Permissions.SendMessages,
                Permissions.ManageCars,
                Permissions.ViewCars
            ],
            UserRole.Contabil =>
            [
                Permissions.ViewAssignedClients,
                Permissions.ViewAllDocuments,
                Permissions.DownloadDocuments,
                Permissions.SendMessages,
                Permissions.ViewCars
            ],
            UserRole.Client =>
            [
                Permissions.ViewOwnDocuments,
                Permissions.UploadDocuments,
                Permissions.SendMessages,
                Permissions.ViewOwnProfile,
                Permissions.ViewCars
            ],
            _ => []
        };

        return permissions;
    }
}

public static class Permissions
{
    public const string ViewPfaRegistrations = "pfa:view";
    public const string ManagePfaRegistrations = "pfa:manage";
    public const string ManageContabili = "contabili:manage";
    public const string ViewAllDocuments = "documents:view_all";
    public const string ViewOwnDocuments = "documents:view_own";
    public const string UploadDocuments = "documents:upload";
    public const string DownloadDocuments = "documents:download";
    public const string ViewAssignedClients = "clients:view_assigned";
    public const string ViewAllChats = "chats:view_all";
    public const string SendMessages = "chats:send";
    public const string ViewOwnProfile = "profile:view";
    
    public const string ManageCars = "cars:manage";
    public const string ViewCars = "cars:view";
}

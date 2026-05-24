using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Notifications.RecurringDocumentation;

internal sealed class AdminTestRecurringDocumentationNotificationsCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext,
    ICommandHandler<SendRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult> sendHandler)
    : ICommandHandler<AdminTestRecurringDocumentationNotificationsCommand, SendRecurringDocumentationNotificationsResult>
{
    public async Task<Result<SendRecurringDocumentationNotificationsResult>> Handle(
        AdminTestRecurringDocumentationNotificationsCommand request,
        CancellationToken cancellationToken)
    {
        User? caller = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userContext.UserId, cancellationToken);

        if (caller is null || caller.Role != UserRole.Admin)
        {
            return Result.Failure<SendRecurringDocumentationNotificationsResult>(UserErrors.Unauthorized());
        }

        return await sendHandler.Handle(
            new SendRecurringDocumentationNotificationsCommand(
                TargetUserId: null,
                RequireFirstOfMonth: false,
                ForceResend: true),
            cancellationToken);
    }
}

using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.FiscalProfiles;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.FiscalProfiles;

public sealed record AdminCallTaskResponse(
    Guid Id,
    Guid PfaRegistrationId,
    Guid PfaUserId,
    string PfaName,
    string? Email,
    string? Phone,
    int TaxYear,
    string Reason,
    /// <summary><c>OPEN</c>, <c>DONE</c>, <c>RESCHEDULED</c>, <c>RESOLVED_BY_COMPLETION</c>.</summary>
    string State,
    Guid? OwnerUserId,
    string? OwnerName,
    string? CallOutcome,
    DateTime? RescheduledToUtc,
    DateTime CreatedAtUtc,
    DateTime? ClosedAtUtc,
    string ProfileStatus);

public sealed record GetAdminCallTasksQuery(string? Type, bool IncludeClosed) : IQuery<IReadOnlyList<AdminCallTaskResponse>>;

public sealed record UpdateAdminCallTaskCommand(
    Guid TaskId,
    string? State,
    string? CallOutcome,
    DateTime? RescheduledToUtc,
    bool AssignToMe) : ICommand<AdminCallTaskResponse>;

internal static class AdminCallTaskMapping
{
    public static string StateCode(AdminCallTaskState state) => state switch
    {
        AdminCallTaskState.Done => "DONE",
        AdminCallTaskState.Rescheduled => "RESCHEDULED",
        AdminCallTaskState.ResolvedByCompletion => "RESOLVED_BY_COMPLETION",
        _ => "OPEN",
    };

    public static AdminCallTaskState? ParseState(string? code) => code switch
    {
        "OPEN" => AdminCallTaskState.Open,
        "DONE" => AdminCallTaskState.Done,
        "RESCHEDULED" => AdminCallTaskState.Rescheduled,
        _ => null,
    };

    public static async Task<List<AdminCallTaskResponse>> ProjectAsync(
        IApplicationDbContext context,
        IQueryable<AdminCallTask> tasks,
        CancellationToken cancellationToken)
    {
        var rows = await tasks
            .Join(context.PfaRegistrations, t => t.PfaRegistrationId, p => p.Id, (t, p) => new
            {
                Task = t,
                p.UserId,
                p.LegalName,
                p.HolderName,
                p.FullName,
                p.Phone,
                p.User.Email,
                p.User.FirstName,
                p.User.LastName,
                p.User.PhoneNumber,
                ProfileStatus = context.PfaTaxProfiles
                    .Where(x => x.PfaRegistrationId == p.Id && x.TaxYear == t.TaxYear)
                    .Select(x => (PfaTaxProfileStatus?)x.Status)
                    .FirstOrDefault(),
                OwnerName = context.Users
                    .Where(u => u.Id == t.OwnerUserId)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefault(),
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AdminCallTaskResponse(
                r.Task.Id,
                r.Task.PfaRegistrationId,
                r.UserId,
                r.LegalName ?? r.HolderName ?? r.FullName ?? $"{r.FirstName} {r.LastName}".Trim(),
                r.Email,
                r.Phone ?? r.PhoneNumber,
                r.Task.TaxYear,
                r.Task.Reason,
                StateCode(r.Task.State),
                r.Task.OwnerUserId,
                r.OwnerName,
                r.Task.CallOutcome,
                r.Task.RescheduledToUtc,
                r.Task.CreatedAtUtc,
                r.Task.ClosedAtUtc,
                FiscalProfileService.StatusCode(r.ProfileStatus ?? PfaTaxProfileStatus.NotStarted)))
            .ToList();
    }
}

internal sealed class GetAdminCallTasksQueryHandler(IApplicationDbContext context)
    : IQueryHandler<GetAdminCallTasksQuery, IReadOnlyList<AdminCallTaskResponse>>
{
    public async Task<Result<IReadOnlyList<AdminCallTaskResponse>>> Handle(
        GetAdminCallTasksQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<AdminCallTask> tasks = context.AdminCallTasks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            tasks = tasks.Where(t => t.Reason == query.Type);
        }

        if (!query.IncludeClosed)
        {
            tasks = tasks.Where(t => t.State == AdminCallTaskState.Open || t.State == AdminCallTaskState.Rescheduled);
        }

        tasks = tasks.OrderBy(t => t.State).ThenBy(t => t.CreatedAtUtc).Take(500);

        return await AdminCallTaskMapping.ProjectAsync(context, tasks, cancellationToken);
    }
}

internal sealed class UpdateAdminCallTaskCommandHandler(IApplicationDbContext context, FiscalProfileService service)
    : ICommandHandler<UpdateAdminCallTaskCommand, AdminCallTaskResponse>
{
    public async Task<Result<AdminCallTaskResponse>> Handle(UpdateAdminCallTaskCommand command, CancellationToken cancellationToken)
    {
        AdminCallTask? task = await context.AdminCallTasks.SingleOrDefaultAsync(t => t.Id == command.TaskId, cancellationToken);
        if (task is null)
        {
            return Result.Failure<AdminCallTaskResponse>(Error.NotFound("AdminCallTask.NotFound", "Sarcina nu există."));
        }

        if (task.State == AdminCallTaskState.ResolvedByCompletion)
        {
            return Result.Failure<AdminCallTaskResponse>(Error.Conflict(
                "AdminCallTask.Resolved", "PFA-ul a completat profilul; sarcina e deja închisă."));
        }

        if (command.CallOutcome?.Length > 2000)
        {
            return Result.Failure<AdminCallTaskResponse>(Error.Problem(
                "AdminCallTask.OutcomeTooLong", "Rezultatul apelului e prea lung (maximum 2000 de caractere)."));
        }

        if (command.State is not null)
        {
            AdminCallTaskState? state = AdminCallTaskMapping.ParseState(command.State);
            if (state is null)
            {
                return Result.Failure<AdminCallTaskResponse>(Error.Problem("AdminCallTask.InvalidState", "Starea nu e validă."));
            }

            if (state == AdminCallTaskState.Rescheduled && command.RescheduledToUtc is null)
            {
                return Result.Failure<AdminCallTaskResponse>(Error.Problem(
                    "AdminCallTask.RescheduleDateRequired", "Alege când se reia apelul."));
            }

            task.State = state.Value;
            task.ClosedAtUtc = state == AdminCallTaskState.Done ? service.UtcNow : null;
            task.RescheduledToUtc = state == AdminCallTaskState.Rescheduled ? command.RescheduledToUtc : null;
            task.OwnerUserId ??= service.CallerId;
        }

        if (command.CallOutcome is not null)
        {
            task.CallOutcome = string.IsNullOrWhiteSpace(command.CallOutcome) ? null : command.CallOutcome.Trim();
        }

        if (command.AssignToMe)
        {
            task.OwnerUserId = service.CallerId;
        }

        await context.SaveChangesAsync(cancellationToken);

        List<AdminCallTaskResponse> projected = await AdminCallTaskMapping.ProjectAsync(
            context, context.AdminCallTasks.Where(t => t.Id == task.Id), cancellationToken);
        return projected[0];
    }
}

using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Create;

internal sealed class CreatePfaRegistrationCommandHandler(IApplicationDbContext context)
    : ICommandHandler<CreatePfaRegistrationCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreatePfaRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        bool exists = await context.PfaRegistrations
            .AnyAsync(r => r.UserId == command.UserId, cancellationToken);

        if (exists)
        {
            return Result.Failure<Guid>(PfaRegistrationErrors.AlreadyExists);
        }

        // Load balancing: assign the PFA to a Contabil
        var contabili = await context.Users
            .Where(u => u.Role == UserRole.Contabil)
            .Select(u => new
            {
                User = u,
                AssignedCount = context.PfaRegistrations.Count(p => p.AssignedContabilId == u.Id)
            })
            .ToListAsync(cancellationToken);

        User? assignedContabil = contabili
            .OrderBy(c => c.AssignedCount)
            .ThenBy(c => c.User.CreatedAtUtc)
            .Select(c => c.User)
            .FirstOrDefault();

        var registration = new PfaRegistration
        {
            Id = Guid.NewGuid(),
            UserId = command.UserId,
            RegistrationType = command.RegistrationType,
            FullName = command.FullName,
            Phone = command.Phone,
            ContractDuration = command.ContractDuration,
            Street = command.Street,
            Number = command.Number,
            City = command.City,
            County = command.County,
            IsOwner = command.IsOwner,
            Status = PfaRegistrationStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            AssignedContabilId = assignedContabil?.Id
        };

        context.PfaRegistrations.Add(registration);

        await context.SaveChangesAsync(cancellationToken);

        return registration.Id;
    }
}

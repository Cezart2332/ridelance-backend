using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal sealed class VerifyBankDeclarationCommandHandler(IApplicationDbContext context)
    : ICommandHandler<VerifyBankDeclarationCommand>
{
    public async Task<Result> Handle(VerifyBankDeclarationCommand command, CancellationToken cancellationToken)
    {
        PfaBankAccountDeclaration? declaration = await context.PfaBankAccountDeclarations
            .SingleOrDefaultAsync(d => d.PfaRegistrationId == command.RegistrationId, cancellationToken);

        if (declaration is null)
        {
            return Result.Failure(Step2Errors.BankDeclarationNotFound);
        }

        declaration.Status = command.Status;
        declaration.AdminNote = command.AdminNote;
        declaration.UpdatedAtUtc = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

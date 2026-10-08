using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.AnafMandate;

/// <summary>
/// Adminul regenerează împuternicirea ANAF după ce a completat ce lipsea (CNP, sediu, CUI). Numărul
/// mandatului rămâne același.
/// </summary>
public sealed record RegenerateAnafMandateCommand(Guid RegistrationId) : ICommand<AnafMandateResult>;

internal sealed class RegenerateAnafMandateCommandHandler(AnafMandateService anafMandate)
    : ICommandHandler<RegenerateAnafMandateCommand, AnafMandateResult>
{
    public async Task<Result<AnafMandateResult>> Handle(
        RegenerateAnafMandateCommand command,
        CancellationToken cancellationToken)
    {
        AnafMandateResult? result = await anafMandate.GenerateAsync(command.RegistrationId, cancellationToken);

        return result is null
            ? Result.Failure<AnafMandateResult>(PfaRegistrationErrors.NotFound(command.RegistrationId))
            : Result.Success(result);
    }
}

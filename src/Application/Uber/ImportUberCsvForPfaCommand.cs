using Application.Abstractions.Authentication;
using Application.Abstractions.Data;
using Application.Abstractions.Messaging;
using Domain.PfaRegistrations;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Uber;

/// <summary>
/// Importul raportului Uber făcut de un operator (admin sau contabil) în contul unui client.
/// Rapoartele Uber vin pe e-mail către birou, nu către șofer, așa că încărcarea lor e o
/// operațiune de back-office — clientul le vede doar ca date pe dashboard.
/// </summary>
public sealed record ImportUberCsvForPfaCommand(
    Guid PfaRegistrationId,
    IReadOnlyList<UberCsvUpload> Files,
    int? Year,
    int? Month) : ICommand<UberDashboardResponse>;

internal sealed class ImportUberCsvForPfaCommandHandler(
    IApplicationDbContext context,
    IUserContext userContext) : ICommandHandler<ImportUberCsvForPfaCommand, UberDashboardResponse>
{
    public async Task<Result<UberDashboardResponse>> Handle(
        ImportUberCsvForPfaCommand command,
        CancellationToken cancellationToken)
    {
        PfaRegistration? pfa = await context.PfaRegistrations
            .FirstOrDefaultAsync(p => p.Id == command.PfaRegistrationId, cancellationToken);

        if (pfa is null)
        {
            return Result.Failure<UberDashboardResponse>(
                Error.NotFound("Pfa.NotFound", "Înregistrarea PFA nu a fost găsită."));
        }

        return await UberCsvImporter.ImportAsync(
            context,
            pfa,
            command.Files,
            command.Year,
            command.Month,
            actorUserId: userContext.UserId,
            cancellationToken);
    }
}

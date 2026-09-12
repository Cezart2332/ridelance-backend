using Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Application.Banking.Queries;

/// <summary>
/// Al cui cont bancar are voie să vadă cel care întreabă.
///
/// Datele bancare sunt cele mai sensibile din platformă, iar de acum le citește și contabilul, nu
/// doar clientul. Regula trăiește într-un singur loc, ca ea să nu se rescrie ușor diferit la
/// fiecare interogare nouă: fiecare handler care atinge tranzacții trece pe aici.
///
/// Legătura care dă dreptul e <c>PfaRegistration.AssignedContabilId</c> — aceeași pe care o
/// folosesc chatul și statisticile contabilului. Când clientul își schimbă contabilul, accesul
/// celui vechi se închide în aceeași clipă, fără nimic de revocat de mână.
/// </summary>
internal static class BankAccess
{
    private static readonly Error NotAllowed = Error.Problem(
        "Bank.NotAllowed",
        "Nu ai acces la datele bancare ale acestui client.");

    /// <summary>
    /// Id-ul utilizatorului ale cărui date se citesc. Fără <paramref name="targetUserId"/> e chiar
    /// cel care întreabă; cu el, doar dacă îi e contabil.
    /// </summary>
    public static async Task<Result<Guid>> ResolveAsync(
        IApplicationDbContext context,
        Guid requesterId,
        Guid? targetUserId,
        CancellationToken cancellationToken)
    {
        if (targetUserId is not Guid target || target == requesterId)
        {
            return requesterId;
        }

        bool isAssignedAccountant = await context.PfaRegistrations
            .AnyAsync(p => p.UserId == target && p.AssignedContabilId == requesterId, cancellationToken);

        return isAssignedAccountant ? target : Result.Failure<Guid>(NotAllowed);
    }
}

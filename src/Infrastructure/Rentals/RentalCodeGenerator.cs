using Application.Abstractions.Services;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Rentals;

/// <summary>
/// Numerotarea vine dintr-o secvență Postgres, singurul lucru din stivă care poate promite unicitate
/// sub cereri simultane fără să blocheze tabela.
/// </summary>
internal sealed class RentalCodeGenerator(ApplicationDbContext context) : IRentalCodeGenerator
{
    /// <remarks>
    /// Aliasul <c>Value</c> nu e cosmetic: <c>SqlQuery&lt;T&gt;</c> împachetează SQL-ul într-o
    /// subinterogare și citește din ea o coloană numită exact așa — <c>SELECT s."Value" FROM (…) AS s</c>.
    /// Fără alias, coloana se cheamă <c>nextval</c>, iar Postgres răspunde
    /// <c>42703: column s.Value does not exist</c>, adică nicio închiriere nu se poate crea.
    /// </remarks>
    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        long next = await context.Database
            .SqlQuery<long>($"SELECT nextval('public.rental_public_code_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);

        // Șase cifre acoperă 999.999 de închirieri; peste, formatul crește singur în loc să taie.
        return $"RL-{next:D6}";
    }
}

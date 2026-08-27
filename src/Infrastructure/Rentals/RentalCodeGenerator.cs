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
    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        long next = await context.Database
            .SqlQuery<long>($"SELECT nextval('public.rental_public_code_seq')")
            .SingleAsync(cancellationToken);

        // Șase cifre acoperă 999.999 de închirieri; peste, formatul crește singur în loc să taie.
        return $"RL-{next:D6}";
    }
}

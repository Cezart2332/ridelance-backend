using Application.Abstractions.Services;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Dossiers;

/// <summary>Numerotarea împuternicirilor ANAF, din secvența <c>anaf_mandate_number_seq</c>.</summary>
internal sealed class AnafMandateNumberGenerator(ApplicationDbContext context) : IAnafMandateNumberGenerator
{
    /// <remarks>Aliasul <c>Value</c> e cerut de <c>SqlQuery&lt;T&gt;</c> (vezi <c>RentalCodeGenerator</c>).</remarks>
    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        long next = await context.Database
            .SqlQuery<long>($"SELECT nextval('public.anaf_mandate_number_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return $"ANAF-{next:D6}";
    }
}

namespace Application.Abstractions.Services;

/// <summary>
/// Următorul număr de împuternicire ANAF: <c>ANAF-000001</c>. Din secvență Postgres, din același
/// motiv ca la <see cref="IRentalCodeGenerator"/>: două trimiteri simultane nu primesc același număr.
/// </summary>
public interface IAnafMandateNumberGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}

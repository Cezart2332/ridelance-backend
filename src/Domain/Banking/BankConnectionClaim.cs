using Domain.Users;
using SharedKernel;

namespace Domain.Banking;

/// <summary>Cum a ajuns conexiunea la un proprietar.</summary>
public enum BankClaimMode
{
    /// <summary>O singură conexiune nouă, un singur link în așteptare — atribuire fără echivoc.</summary>
    Auto = 0,

    /// <summary>Au fost mai mulți candidați și utilizatorul a ales.</summary>
    Manual = 1,
}

/// <summary>
/// Jurnalul atribuirilor de conexiuni bancare.
///
/// Providerul lucrează cu un singur cont pentru toți clienții, iar linkul de conectare nu poate
/// purta nicio referință de-a noastră. Rezultă că proprietarul unei conexiuni e o deducție, nu
/// un fapt primit — iar deducțiile trebuie să lase urmă. Tabela e și registrul de revendicări:
/// indexul unic pe <see cref="ProviderConnectionId"/> face imposibilă atribuirea aceleiași
/// conexiuni la doi utilizatori, chiar dacă logica de deasupra ar greși.
/// </summary>
public sealed class BankConnectionClaim : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BankConnectionId { get; set; }

    /// <summary>Id-ul de la provider, în clar — se compară cu lista lui la fiecare revendicare.</summary>
    public string ProviderConnectionId { get; set; } = string.Empty;

    public BankClaimMode Mode { get; set; }

    /// <summary>Câți candidați erau când s-a făcut atribuirea. 1 la Auto; mai mulți la Manual.</summary>
    public int CandidateCount { get; set; }

    public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public BankConnection Connection { get; set; } = null!;
}

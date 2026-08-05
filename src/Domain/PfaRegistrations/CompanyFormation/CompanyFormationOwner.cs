using SharedKernel;

namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>
/// Proprietarul imobilului declarat ca sediu social, când nu e solicitantul însuși.
/// Pot fi mai mulți (coproprietate), toți cu aceleași date ca solicitantul.
/// </summary>
public sealed class CompanyFormationOwner : Entity
{
    public Guid Id { get; set; }
    public Guid CompanyFormationRequestId { get; set; }

    /// <summary>Ordinea de afișare, ca lista să nu se rearanjeze între încărcări.</summary>
    public int Position { get; set; }

    public PersoanaFizica Persoana { get; set; } = new();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public CompanyFormationRequest CompanyFormationRequest { get; set; } = null!;
}

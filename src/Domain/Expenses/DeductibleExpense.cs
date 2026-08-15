using Domain.Documents;
using Domain.PfaRegistrations;
using Domain.Users;
using SharedKernel;

namespace Domain.Expenses;

public sealed class DeductibleExpense : Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public Guid DocumentId { get; set; }
    public string CatalogCategory { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string DeductibleLabel { get; set; } = string.Empty;
    public decimal? AmountRon { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }

    /// <summary>Data de pe document. `Year`/`Month` rămân perioada contabilă a cheltuielii.</summary>
    public DateOnly? ExpenseDate { get; set; }

    public string? SupplierName { get; set; }

    /// <summary>TVA-ul de pe document. Nu poate depăși suma totală — se validează la salvare.</summary>
    public decimal? VatAmount { get; set; }

    public string Currency { get; set; } = "RON";

    /// <summary>„Bon fiscal", „Factură" — cum s-a numit actul, nu ce categorie fiscală are.</summary>
    public string? DocumentTypeLabel { get; set; }

    public ExpenseSource Source { get; set; } = ExpenseSource.Manual;
    public ExpenseStatus Status { get; set; } = ExpenseStatus.Confirmed;

    /// <summary>
    /// Ce a citit modelul, câmp cu câmp, cu tot cu încrederea per câmp. Se păstrează brut ca
    /// să se poată vedea ulterior de unde a venit o valoare greșită — nu se folosește în calcule.
    /// </summary>
    public string? ExtractionJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }

    public User User { get; set; } = null!;
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public Document Document { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}

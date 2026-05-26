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
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }

    public User User { get; set; } = null!;
    public PfaRegistration PfaRegistration { get; set; } = null!;
    public Document Document { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}

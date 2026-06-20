using Domain.Documents;
using SharedKernel;

namespace Domain.PfaRegistrations;

public sealed class PfaFiscalProfile : Entity
{
    public Guid Id { get; set; }
    public Guid PfaRegistrationId { get; set; }
    public PfaTaxationSystem TaxationSystem { get; set; } = PfaTaxationSystem.RealSystem;
    public bool IsVatPayer { get; set; }
    public bool HasEmployees { get; set; }
    public PfaAccountingRegime AccountingRegime { get; set; } = PfaAccountingRegime.SingleEntry;
    public PfaSpecialVatCodeStatus SpecialVatCodeStatus { get; set; } = PfaSpecialVatCodeStatus.ToVerify;
    public DateTime? SpecialVatCodeObtainedAtUtc { get; set; }
    public Guid? SpecialVatCodeDocumentId { get; set; }
    public PfaPlatformStatus UberStatus { get; set; } = PfaPlatformStatus.Inactive;
    public PfaPlatformStatus BoltStatus { get; set; } = PfaPlatformStatus.Inactive;
    public PfaTriStateStatus OtherPlatformsStatus { get; set; } = PfaTriStateStatus.No;
    public PfaTriStateStatus CashRevenueStatus { get; set; } = PfaTriStateStatus.ToVerify;
    public PfaCashRegisterStatus CashRegisterStatus { get; set; } = PfaCashRegisterStatus.ToVerify;
    public PfaVehicleUsageType VehicleUsageType { get; set; } = PfaVehicleUsageType.OwnCar;
    public string? VehicleSupportingDocumentLabel { get; set; }
    public Guid? VehicleSupportingDocumentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedByUserId { get; set; }

    public PfaRegistration PfaRegistration { get; set; } = null!;
    public Document? SpecialVatCodeDocument { get; set; }
    public Document? VehicleSupportingDocument { get; set; }
}

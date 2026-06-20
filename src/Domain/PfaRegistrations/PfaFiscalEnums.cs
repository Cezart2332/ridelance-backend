namespace Domain.PfaRegistrations;

public enum PfaTaxationSystem
{
    RealSystem = 0
}

public enum PfaAccountingRegime
{
    SingleEntry = 0
}

public enum PfaSpecialVatCodeStatus
{
    Yes = 0,
    No = 1,
    InProgress = 2,
    ToVerify = 3
}

public enum PfaPlatformStatus
{
    Active = 0,
    Inactive = 1,
    Activating = 2,
    Suspended = 3
}

public enum PfaTriStateStatus
{
    Yes = 0,
    No = 1,
    ToVerify = 2
}

public enum PfaCashRegisterStatus
{
    Yes = 0,
    No = 1,
    NotApplicable = 2,
    ToVerify = 3
}

public enum PfaVehicleUsageType
{
    OwnCar = 0,
    RentedCar = 1,
    CommodatumCar = 2,
    Other = 3
}

public enum PfaPlatformProvider
{
    Uber = 0,
    Bolt = 1
}

public enum PfaPlatformAccountKind
{
    Driver = 0,
    Fleet = 1
}

public enum PfaFleetAccountStatus
{
    NotConfigured = 0,
    InProgress = 1,
    Configured = 2
}

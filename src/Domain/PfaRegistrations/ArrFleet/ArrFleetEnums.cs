namespace Domain.PfaRegistrations.ArrFleet;

/// <summary>
/// Procedura de după trimitere, setată manual de admin și văzută de client în dashboard. Ordinea
/// valorilor e ordinea procedurii.
/// </summary>
public enum ArrFleetStatus
{
    /// <summary>Clientul încă completează pasul. Nu face parte din procedură.</summary>
    Draft = 0,
    DocumentsSubmitted = 1,
    InReview = 2,
    /// <summary>Contul ARR și conturile de flotă sunt în lucru.</summary>
    InProgress = 3,
    AuthorizationIssued = 4,
    CertifiedCopyIssued = 5,
    BadgesIssued = 6,
    Completed = 7,
}

/// <summary>Platformele alese de client. Cel puțin una la trimitere.</summary>
[Flags]
public enum ArrFleetPlatforms
{
    None = 0,
    Uber = 1,
    Bolt = 2,
}

/// <summary>Cum deține clientul mașina cu care lucrează. Decide contractul cerut.</summary>
public enum ArrFleetVehicleOwnership
{
    /// <summary>Proprietate: niciun contract.</summary>
    Ownership = 0,
    /// <summary>Comodat autentificat la notariat.</summary>
    Loan = 1,
    /// <summary>Contract de închiriere semnat de ambele părți.</summary>
    Rental = 2,
    Leasing = 3,
}

/// <summary>Documentele oficiale obținute de agent și încărcate din admin.</summary>
public enum ArrFleetOfficialDocument
{
    TransportAuthorization = 0,
    CertifiedCopy = 1,
    UberBadge = 2,
    BoltBadge = 3,
}

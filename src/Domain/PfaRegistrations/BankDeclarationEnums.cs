namespace Domain.PfaRegistrations;

/// <summary>Starea declarației de cont bancar (Pasul 2.3). Avans manual din admin.</summary>
public enum BankDeclarationStatus
{
    Pending = 0,
    Verified = 1,
    Rejected = 2,
}

/// <summary>De unde provin datele contului bancar.</summary>
public enum BankDeclarationSource
{
    /// <summary>Introdus manual de client + document de confirmare.</summary>
    Manual = 0,
    /// <summary>Precompletat dintr-o conexiune PSD2 activă (open banking).</summary>
    OpenBanking = 1,
}

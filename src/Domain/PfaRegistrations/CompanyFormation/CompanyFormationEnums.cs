namespace Domain.PfaRegistrations.CompanyFormation;

/// <summary>Starea dosarului de înființare a societății prin partener (Consulto).</summary>
public enum CompanyFormationStatus
{
    /// <summary>În completare de către șofer. Se șterge automat după 90 de zile de inactivitate.</summary>
    Draft = 0,
    /// <summary>Semnat și trimis. Formularul devine read-only pentru șofer.</summary>
    Submitted = 1,
    InReviewConsulto = 2,
    /// <summary>Adminul a cerut corecturi; se redeschid doar etapele indicate.</summary>
    InfoRequested = 3,
    Approved = 4,
    Rejected = 5,

    /// <summary>
    /// Semnat, dar neplătit. Dosarul e complet și blocat la editare, iar mai departe nu pleacă
    /// nimic: plata e condiția de trimitere, nu un pas de după ea.
    /// </summary>
    AwaitingPayment = 6,

    /// <summary>
    /// Plata a fost confirmată de Stripe (webhook, server-side). Singura stare din care dosarul
    /// are voie să plece spre Consulto.
    /// </summary>
    PaymentConfirmed = 7,

    /// <summary>Arhiva a plecat pe email spre Consulto. Trimiterea nu se mai repetă.</summary>
    SentToConsulto = 8,
}

/// <summary>Tipul actului de identitate al unei persoane fizice din dosar.</summary>
public enum TipActIdentitate
{
    CI = 0,
    CIE = 1,
    BI = 2,
    PasaportStrain = 3,
    PermisSedere = 4,
}

/// <summary>De unde vine adresa sediului social.</summary>
public enum RegisteredOfficeType
{
    /// <summary>Adresă pusă la dispoziție de Consulto, aleasă dintr-o listă.</summary>
    ConsultoProvided = 0,
    /// <summary>Adresă proprie a solicitantului sau a unui terț proprietar.</summary>
    Own = 1,
}

/// <summary>Etapa la care a ajuns dosarul — folosită pentru „resume" și pentru gating pe server.</summary>
public enum CompanyFormationStage
{
    PersonalData = 1,
    RegisteredOffice = 2,
    Consent = 3,
}

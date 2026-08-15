namespace Application.Abstractions.Services;

/// <summary>
/// Autorizarea prin redirect, în stilul PSD2 clasic: ceri o „requisition", trimiți utilizatorul
/// la bancă, iar acesta se întoarce la tine cu o referință și eventual un cod de schimbat.
///
/// <b>Nu are nicio implementare în acest moment.</b> Providerul activ (Fintable) mintează un
/// link care se termină la el, fără să ne întoarcă nimic — deci nu are ce implementa aici.
/// Interfața e păstrată deliberat (decizie din 15.08.2026) pentru un viitor provider care
/// chiar folosește fluxul cu redirect; până atunci, nu o căuta implementată.
///
/// Providerii care o implementează o vor face pe lângă <see cref="IBankDataProvider"/>,
/// nu în locul ei.
/// </summary>
public interface IBankRedirectAuthorization
{
    /// <param name="redirectAddress">Unde întoarce providerul utilizatorul după autorizare.</param>
    /// <param name="reference">Token-ul nostru, care trebuie să revină în redirect.</param>
    Task<BankRequisitionCreated> CreateRequisitionAsync(
        string institutionId,
        string redirectAddress,
        string reference,
        int maxHistoricalDays,
        int accessValidForDays,
        CancellationToken cancellationToken = default);

    /// <param name="authorizationCode">Codul de unică folosință întors la redirect, dacă providerul folosește schimb de cod.</param>
    Task<BankRequisitionDetails> GetRequisitionAsync(
        string requisitionId,
        string? authorizationCode = null,
        CancellationToken cancellationToken = default);
}

public sealed record BankRequisitionCreated(
    string RequisitionId,
    string? AgreementId,
    string AuthorizationLink);

public enum BankRequisitionStatus
{
    Created,
    GivingConsent,
    UndergoingAuthentication,
    Linked,
    Expired,
    Rejected,
    Suspended,
}

/// <param name="UpdatedRequisitionId">
/// Setat când providerul schimbă identificatorul în timpul finalizării; se persistă în locul celui vechi.
/// </param>
public sealed record BankRequisitionDetails(
    BankRequisitionStatus Status,
    IReadOnlyList<string> AccountIds,
    DateTime? ConsentExpiresAtUtc,
    string? UpdatedRequisitionId = null);

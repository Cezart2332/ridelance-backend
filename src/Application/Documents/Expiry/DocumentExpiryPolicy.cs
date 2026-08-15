using Application.Documents.AiVerification;
using Domain.Documents;

namespace Application.Documents.Expiry;

/// <summary>Starea temporală a unui document care are dată de expirare.</summary>
public enum DocumentExpiryState
{
    /// <summary>Categoria nu expiră, sau documentul n-are dată de expirare înregistrată.</summary>
    NotApplicable = 0,
    Valid = 1,
    ExpiringSoon = 2,
    Expired = 3,
}

/// <param name="DaysUntilExpiry">Negativ după expirare. Null când starea e <c>NotApplicable</c>.</param>
public sealed record DocumentExpiry(DocumentExpiryState State, int? DaysUntilExpiry, DateOnly? ExpiresOn);

/// <summary>
/// Ce documente expiră și când devine expirarea o problemă.
///
/// Calculul stă aici, pe server, nu în frontend: „expiră în 34 de zile" e aritmetică de
/// calendar în fusul României, iar clientul are alt ceas și alt fus. Frontendul primește
/// starea gata calculată și o afișează.
///
/// Lista categoriilor expirabile era duplicată — o dată în jobul de notificări, o dată în
/// `src/constants/documentSections.tsx`. Aici e sursa unică; jobul o folosește, iar clientul
/// o primește prin endpoint.
/// </summary>
public static class DocumentExpiryPolicy
{
    /// <summary>
    /// Pragul de la care un document „expiră curând". Unul singur pentru toate tipurile, cât
    /// timp nu există o cerință per tip — dar ținut într-un singur loc, ca diferențierea să fie
    /// o schimbare de tabel, nu una de logică.
    /// </summary>
    public const int ExpiringSoonDays = 30;

    public static readonly IReadOnlySet<DocumentCategory> ExpirableCategories = new HashSet<DocumentCategory>
    {
        DocumentCategory.Buletin,
        DocumentCategory.CarteIdentitate,
        DocumentCategory.PermisConducere,
        DocumentCategory.AtestatTransport,
        DocumentCategory.AtestatSofer,
        DocumentCategory.CazierJudiciar,
        DocumentCategory.AdeverintaMedicala,
        DocumentCategory.AvizPsihologic,
        DocumentCategory.ITP,
        DocumentCategory.Talon,
        DocumentCategory.RCA,
        DocumentCategory.Casco,
        DocumentCategory.AsigurareCalatori,
        DocumentCategory.CopieConforma,
        DocumentCategory.EcusonUber,
        DocumentCategory.EcusonBolt,
        DocumentCategory.ContractVehicul,
    };

    public static bool Expires(DocumentCategory category) => ExpirableCategories.Contains(category);

    /// <param name="today">Ziua de referință; în producție <see cref="DocumentDateValidator.TodayInRomania"/>.</param>
    public static DocumentExpiry Evaluate(DocumentCategory category, DateTime? expiresAtUtc, DateOnly today)
    {
        if (!Expires(category) || expiresAtUtc is null)
        {
            return new DocumentExpiry(DocumentExpiryState.NotApplicable, null, null);
        }

        var expiresOn = DateOnly.FromDateTime(expiresAtUtc.Value);
        int days = expiresOn.DayNumber - today.DayNumber;

        // Ziua expirării încă e o zi validă: un RCA care expiră azi acoperă ziua de azi.
        DocumentExpiryState state = days switch
        {
            < 0 => DocumentExpiryState.Expired,
            <= ExpiringSoonDays => DocumentExpiryState.ExpiringSoon,
            _ => DocumentExpiryState.Valid,
        };

        return new DocumentExpiry(state, days, expiresOn);
    }
}

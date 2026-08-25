namespace Application.Abstractions.Services;

/// <summary>
/// Datele publice ale unei firme, după CUI.
/// </summary>
/// <remarks>
/// Sursa e registrul ANAF, care e public și nu cere autentificare. Serviciul există ca să nu fie
/// nevoie ca cineva să transcrie manual denumirea, adresa și numărul de la Registrul Comerțului
/// pe fiecare factură — transcrise de mână, ajung greșite pe o factură care nu se mai poate
/// corecta decât prin storno.
/// </remarks>
public interface ICompanyLookupService
{
    /// <summary>
    /// Caută firma după CUI. <see langword="null" /> dacă registrul n-o are — un CUI inexistent
    /// nu e o eroare de sistem, e un CUI greșit.
    /// </summary>
    Task<CompanyLookupResult?> FindByCuiAsync(string cui, CancellationToken cancellationToken = default);
}

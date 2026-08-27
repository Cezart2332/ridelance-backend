using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;

namespace Application.Rentals.Documents;

/// <summary>Ce document se generează. Fiecare cere alte date.</summary>
public enum RentalDocumentType
{
    /// <summary>Contractul de închiriere.</summary>
    RentalContract,

    /// <summary>Procesul-verbal de predare, la începutul închirierii.</summary>
    HandoverProtocol,

    /// <summary>Procesul-verbal de primire, la returnare.</summary>
    ReturnProtocol,
}

/// <param name="Field">Cheia câmpului, ca interfața să știe ce să deschidă: `car.plateNumber`.</param>
/// <param name="Label">Cum se numește în formular.</param>
/// <param name="Owner">`car`, `company` sau `tenant` — unde se completează.</param>
public sealed record MissingField(string Field, string Label, string Owner);

/// <summary>
/// Ce mai trebuie completat înainte să se poată genera un document.
/// </summary>
/// <remarks>
/// Spec §5. Rostul e ca interfața să ceară **exact** câmpurile lipsă, nu să trimită omul înapoi în
/// formularul complet de editare a mașinii ca să caute singur ce nu e pus. Un contract fără numărul
/// de înmatriculare nu e un contract; unul fără culoarea mașinii e.
///
/// Verificatorul e o funcție pură peste patru obiecte, fără acces la baza de date: se poate testa
/// direct și dă același răspuns oriunde e chemat.
/// </remarks>
public static class RentalDocumentRequirements
{
    public static IReadOnlyList<MissingField> For(
        RentalDocumentType type,
        Rental rental,
        Car car,
        CompanyProfile? company,
        Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(rental);
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(tenant);

        var missing = new List<MissingField>();

        // Firma. Fără ea nu există parte contractantă, deci nu există document.
        if (company is null)
        {
            missing.Add(new MissingField("company.profile", "Datele firmei", "company"));
        }
        else
        {
            Require(missing, company.LegalName, "company.legalName", "Denumirea firmei", "company");
            Require(missing, company.Cui, "company.cui", "CUI", "company");
            Require(missing, company.RegisteredOffice, "company.registeredOffice", "Sediul social", "company");
            Require(missing, company.LegalRepresentative, "company.legalRepresentative", "Reprezentant legal", "company");
        }

        // Mașina, ca obiect al contractului. Marca și modelul o descriu; numărul și VIN-ul o
        // identifică — un contract fără ele nu spune care mașină s-a predat.
        Require(missing, car.PlateNumber, "car.plateNumber", "Număr de înmatriculare", "car");
        Require(missing, car.Vin, "car.vin", "VIN", "car");

        // Chiriașul. Codul fiscal diferă după tip; se cere doar cel potrivit.
        Require(missing, tenant.Name, "tenant.name", "Numele chiriașului", "tenant");
        Require(missing, tenant.Address, "tenant.address", "Adresa chiriașului", "tenant");

        if (tenant.Type == TenantType.Individual)
        {
            Require(missing, tenant.Cnp, "tenant.cnp", "CNP", "tenant");
            Require(missing, tenant.IdSeries, "tenant.idSeries", "Serie act identitate", "tenant");
            Require(missing, tenant.IdNumber, "tenant.idNumber", "Număr act identitate", "tenant");
        }
        else
        {
            Require(missing, tenant.Cui, "tenant.cui", "CUI chiriaș", "tenant");
        }

        // Procesele-verbale consemnează starea mașinii la predare. Fără kilometraj, documentul nu
        // poate proba nimic despre ce s-a întâmplat între predare și primire.
        if (type is RentalDocumentType.HandoverProtocol or RentalDocumentType.ReturnProtocol
            && rental.StartMileage is null)
        {
            missing.Add(new MissingField("rental.startMileage", "Kilometraj la preluare", "rental"));
        }

        return missing;
    }

    private static void Require(List<MissingField> missing, string? value, string field, string label, string owner)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(new MissingField(field, label, owner));
        }
    }
}

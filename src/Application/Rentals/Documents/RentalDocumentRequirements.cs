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
        Tenant tenant,
        CheckRecord? checkIn = null)
    {
        ArgumentNullException.ThrowIfNull(rental);
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(tenant);

        var missing = new List<MissingField>();
        bool contract = type == RentalDocumentType.RentalContract;

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

            // Doar contractul le tipărește (clauza 1.1); procesele-verbale nu.
            if (contract)
            {
                Require(missing, company.LegalRepresentativeRole, "company.legalRepresentativeRole", "Calitatea reprezentantului legal", "company");
                Require(missing, company.RegCom, "company.regCom", "Nr. Registrul Comerțului", "company");
                Require(missing, company.Iban, "company.iban", "IBAN", "company");
            }
        }

        // Mașina, ca obiect al contractului. Marca și modelul o descriu; numărul și VIN-ul o
        // identifică — un contract fără ele nu spune care mașină s-a predat.
        Require(missing, car.PlateNumber, "car.plateNumber", "Număr de înmatriculare", "car");
        Require(missing, car.Vin, "car.vin", "VIN", "car");

        if (contract && car.Year <= 0)
        {
            missing.Add(new MissingField("car.year", "An fabricație", "car"));
        }

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

            if (contract)
            {
                Require(missing, tenant.RegCom, "tenant.regCom", "Nr. Registrul Comerțului al chiriașului", "tenant");

                // Un SRL semnează prin cineva; la PFA semnează titularul, al cărui nume e chiar
                // denumirea, deci rămâne „după caz”, cum spune șablonul.
                if (tenant.Type == TenantType.Srl)
                {
                    Require(missing, tenant.RepresentativeName, "tenant.representativeName", "Reprezentantul chiriașului", "tenant");
                }
            }
        }

        if (contract)
        {
            RequireContractTerms(missing, rental);
        }

        // Procesele-verbale consemnează starea mașinii la predare. Fără kilometraj, documentul nu
        // poate proba nimic despre ce s-a întâmplat între predare și primire. Predarea făcută
        // propriu-zis (CheckRecord) ține loc de câmpurile de pe închiriere.
        if (type is RentalDocumentType.HandoverProtocol or RentalDocumentType.ReturnProtocol
            && rental.StartMileage is null
            && checkIn is null)
        {
            missing.Add(new MissingField("rental.startMileage", "Kilometraj la preluare", "rental"));
        }

        if (type == RentalDocumentType.HandoverProtocol)
        {
            if (string.IsNullOrWhiteSpace(checkIn?.FuelLevel) && string.IsNullOrWhiteSpace(rental.FuelLevelAtPickup))
            {
                missing.Add(new MissingField("rental.fuelLevelAtPickup", "Nivel combustibil la predare", "rental"));
            }

            if (HandoverState.KeysAtPickup(rental, checkIn) is null)
            {
                missing.Add(new MissingField("rental.keysCount", "Seturi de chei predate", "rental"));
            }
        }

        return missing;
    }

    /// <summary>
    /// Ce s-a convenit și trebuie să apară în contract. Penalitățile și conducătorul suplimentar
    /// nu sunt aici: șablonul le are „dacă părțile au convenit”, iar lipsa lor are o formulare.
    /// </summary>
    private static void RequireContractTerms(List<MissingField> missing, Rental rental)
    {
        if (rental.PaymentDueDay is null)
        {
            missing.Add(new MissingField("rental.paymentDueDay", "Ziua de plată a chiriei", "rental"));
        }

        if (rental.PaymentMethod is null)
        {
            missing.Add(new MissingField("rental.paymentMethod", "Modalitatea de plată", "rental"));
        }

        if (rental.HasKmLimit)
        {
            if (rental.MileageLimit is null or <= 0)
            {
                missing.Add(new MissingField("rental.mileageLimit", "Kilometri incluși pe săptămână", "rental"));
            }

            if (rental.ExtraKmCostBani <= 0)
            {
                missing.Add(new MissingField("rental.extraKmCostBani", "Cost pe km suplimentar", "rental"));
            }
        }

        if (rental.UnavailabilityRule is null)
        {
            missing.Add(new MissingField("rental.unavailabilityRule", "Chiria pe durata indisponibilității", "rental"));
        }

        if (rental.FranchiseBani is null)
        {
            missing.Add(new MissingField("rental.franchiseBani", "Franșiza la daună", "rental"));
        }

        if (rental.NoticeDays is null)
        {
            missing.Add(new MissingField("rental.noticeDays", "Preavizul de denunțare (zile)", "rental"));
        }
    }

    private static void Require(List<MissingField> missing, string? value, string field, string label, string owner)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(new MissingField(field, label, owner));
        }
    }
}

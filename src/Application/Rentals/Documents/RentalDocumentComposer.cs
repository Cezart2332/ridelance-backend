using Application.Abstractions.Dossiers;
using Domain.Cars;
using Domain.Companies;
using Domain.Rentals;

namespace Application.Rentals.Documents;

/// <summary>
/// Compune ce scrie în document din ce știm deja.
/// </summary>
/// <remarks>
/// Regula principală de UX din spec §14: nimic nu se reintroduce. Firma, mașina, chiriașul și
/// termenii sunt deja în sistem — documentul se umple din ele, iar singurul lucru care se cere
/// vreodată manual e ce chiar lipsește, prin `RentalDocumentRequirements`.
/// </remarks>
internal static class RentalDocumentComposer
{
    /// <summary>Linia pe care semnează firma: prima.</summary>
    public const int CompanySignatureSlot = 1;

    /// <summary>Linia pe care semnează chiriașul: a doua, după cea a firmei.</summary>
    public const int TenantSignatureSlot = 2;

    public static RentalDocumentData Compose(
        RentalDocumentType type,
        Rental rental,
        Car car,
        CompanyProfile company,
        Tenant tenant,
        string? conditions)
    {
        string title = type switch
        {
            RentalDocumentType.RentalContract => "Contract de închiriere",
            RentalDocumentType.HandoverProtocol => "Proces-verbal de predare",
            _ => "Proces-verbal de primire",
        };

        List<RentalDocumentSection> sections =
        [
            new("Proprietar", [
                new RentalDocumentField("Denumire", company.LegalName),
                new RentalDocumentField("CUI", company.Cui),
                new RentalDocumentField("Reg. com.", company.RegCom),
                new RentalDocumentField("Sediu social", company.RegisteredOffice),
                new RentalDocumentField("Reprezentant legal", company.LegalRepresentative),
                new RentalDocumentField("Telefon", company.Phone),
                new RentalDocumentField("Email", company.Email),
            ]),
            new("Chiriaș", TenantFields(tenant)),
            new("Vehicul", [
                new RentalDocumentField("Marcă și model", $"{car.Brand} {car.Model}"),
                new RentalDocumentField("An fabricație", car.Year.ToString(PdfCulture)),
                new RentalDocumentField("Număr de înmatriculare", car.PlateNumber),
                new RentalDocumentField("VIN", car.Vin),
                new RentalDocumentField("Culoare", car.Color),
                new RentalDocumentField("Kilometraj la preluare", Km(rental.StartMileage)),
            ]),
        ];

        sections.Add(type == RentalDocumentType.RentalContract
            ? new RentalDocumentSection("Condițiile închirierii", ContractFields(rental))
            : new RentalDocumentSection("Starea la predare", ProtocolFields(rental)));

        // Semnează cele două părți. Numele se tipăresc, ca linia să nu fie anonimă pe hârtie.
        string[] signatures = [company.LegalName, tenant.Name];

        return new RentalDocumentData(title, rental.PublicCode, sections, conditions, signatures, DateTime.UtcNow);
    }

    private static readonly System.Globalization.CultureInfo PdfCulture =
        System.Globalization.CultureInfo.InvariantCulture;

    private static List<RentalDocumentField> TenantFields(Tenant tenant) =>
        tenant.Type == TenantType.Individual
            ? [
                new RentalDocumentField("Nume", tenant.Name),
                new RentalDocumentField("CNP", tenant.Cnp),
                new RentalDocumentField("Act identitate", $"{tenant.IdSeries} {tenant.IdNumber}".Trim()),
                new RentalDocumentField("Permis de conducere", tenant.DriverLicenseNumber),
                new RentalDocumentField("Adresă", tenant.Address),
                new RentalDocumentField("Telefon", tenant.Phone),
                new RentalDocumentField("Email", tenant.Email),
            ]
            : [
                new RentalDocumentField("Denumire", tenant.Name),
                new RentalDocumentField("CUI", tenant.Cui),
                new RentalDocumentField("Reg. com.", tenant.RegCom),
                new RentalDocumentField("Adresă", tenant.Address),
                new RentalDocumentField("Telefon", tenant.Phone),
                new RentalDocumentField("Email", tenant.Email),
            ];

    private static List<RentalDocumentField> ContractFields(Rental rental) =>
    [
        new RentalDocumentField("Perioadă", $"{Date(rental.StartAtUtc)} – {Date(rental.EndAtUtc)}"),
        new RentalDocumentField("Chirie săptămânală", Money(rental.WeeklyRentBani)),
        new RentalDocumentField("Garanție", Money(rental.DepositBani)),
        new RentalDocumentField("Alte costuri", rental.OtherCostsBani > 0 ? Money(rental.OtherCostsBani) : null),
        new RentalDocumentField(
            "Limită de kilometri",
            rental.HasKmLimit ? $"{Km(rental.MileageLimit)} incluși, {Money(rental.ExtraKmCostBani)}/km suplimentar" : "Fără limită"),
        new RentalDocumentField("Regulă combustibil", rental.FuelRule),
    ];

    private static List<RentalDocumentField> ProtocolFields(Rental rental) =>
    [
        new RentalDocumentField("Data predării", Date(rental.StartAtUtc)),
        new RentalDocumentField("Kilometraj", Km(rental.StartMileage)),
        new RentalDocumentField("Nivel combustibil", rental.FuelLevelAtPickup),
        new RentalDocumentField(
            "Accesorii predate",
            rental.Accessories.Count > 0 ? string.Join(", ", rental.Accessories) : null),
        new RentalDocumentField("Alte accesorii", rental.AccessoriesOther),
        new RentalDocumentField("Observații", rental.Notes),
    ];

    private static string Date(DateTime value) => value.ToLocalTime().ToString("dd.MM.yyyy", PdfCulture);

    private static string Money(long bani) => (bani / 100m).ToString("N2", PdfCulture) + " lei";

    private static string? Km(int? value) => value is null ? null : $"{value:N0} km";
}

using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.CompanyFormation;

internal static class CompanyFormationErrors
{
    public static readonly Error NoRegistration = Error.Problem(
        "CompanyFormation.NoRegistration",
        "Nu există un dosar PFA pentru utilizatorul curent.");

    public static readonly Error WrongBranch = Error.Problem(
        "CompanyFormation.WrongBranch",
        "Dosarul de înființare există doar pentru varianta „Nu am PFA”.");

    public static readonly Error Locked = Error.Problem(
        "CompanyFormation.Locked",
        "Dosarul a fost trimis și nu mai poate fi modificat.");

    public static readonly Error InvalidCnp = Error.Problem(
        "CompanyFormation.InvalidCnp",
        "CNP-ul nu este valid. Verifică cele 13 cifre.");

    public static readonly Error ExpiredIdentityCard = Error.Problem(
        "CompanyFormation.ExpiredIdentityCard",
        "Actul de identitate expiră în mai puțin de 30 de zile. Reînnoiește-l înainte de înființare.");

    public static readonly Error PersonalDataIncomplete = Error.Problem(
        "CompanyFormation.PersonalDataIncomplete",
        "Completează întâi datele personale.");

    public static readonly Error RegisteredOfficeIncomplete = Error.Problem(
        "CompanyFormation.RegisteredOfficeIncomplete",
        "Completează întâi datele sediului social.");

    public static readonly Error OwnerNotFound = Error.NotFound(
        "CompanyFormation.OwnerNotFound",
        "Proprietarul nu a fost găsit în acest dosar.");

    public static readonly Error OwnerRequired = Error.Problem(
        "CompanyFormation.OwnerRequired",
        "Trebuie să rămână cel puțin un proprietar declarat.");

    public static readonly Error CnpMissing = Error.NotFound(
        "CompanyFormation.CnpMissing",
        "Persoana nu are un CNP salvat în dosar.");

    public static readonly Error ReasonRequired = Error.Problem(
        "CompanyFormation.ReasonRequired",
        "Motivul redeschiderii dosarului este obligatoriu.");

    public static readonly Error NotSubmitted = Error.Problem(
        "CompanyFormation.NotSubmitted",
        "Dosarul nu a fost încă semnat, deci nu poate fi exportat.");

    public static readonly Error ConsentFlowNotFound = Error.NotFound(
        "CompanyFormation.ConsentFlowNotFound",
        "Textele acordului de consimțământ nu sunt disponibile.");

    public static readonly Error ConsentIncomplete = Error.Problem(
        "CompanyFormation.ConsentIncomplete",
        "Toate declarațiile trebuie acceptate înainte de semnare.");

    public static readonly Error SignatureMissing = Error.Problem(
        "CompanyFormation.SignatureMissing",
        "Semnătura lipsește sau nu a putut fi citită.");

    public static readonly Error SignatureTooLarge = Error.Problem(
        "CompanyFormation.SignatureTooLarge",
        "Imaginea semnăturii este prea mare.");

    // Poarta de trimitere spre Consulto. Conflict (409), nu Validation: cererea e corectă,
    // doar starea dosarului nu o permite încă.
    public static readonly Error PaymentNotConfirmed = Error.Conflict(
        "CompanyFormation.PaymentNotConfirmed",
        "Dosarul nu poate fi trimis: plata avansului nu e confirmată.");

    public static readonly Error AlreadySentToConsulto = Error.Conflict(
        "CompanyFormation.AlreadySentToConsulto",
        "Dosarul a fost deja trimis la Consulto.");

    public static readonly Error NotSigned = Error.Conflict(
        "CompanyFormation.NotSigned",
        "Dosarul nu e semnat, deci nu poate fi trimis.");

    public static readonly Error ConsultoOfficeNotFound = Error.NotFound(
        "CompanyFormation.ConsultoOfficeNotFound",
        "Adresa de sediu aleasă nu mai este disponibilă.");
}

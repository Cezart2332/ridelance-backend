using SharedKernel;

namespace Application.PfaRegistrations.Onboarding.Step2;

internal static class Step2Errors
{
    public static readonly Error NoRegistration = Error.Problem(
        "Onboarding.Step2.NoRegistration",
        "Nu există un dosar PFA pentru utilizatorul curent.");

    public static readonly Error BankDeclarationNotFound = Error.NotFound(
        "Onboarding.Step2.BankNotFound",
        "Nu există o declarație de cont bancar pentru acest dosar.");

    public static readonly Error OblioNotFound = Error.NotFound(
        "Onboarding.Step2.OblioNotFound",
        "Nu există un cont Oblio pentru acest dosar.");

    public static readonly Error SignaturePacketNotFound = Error.NotFound(
        "Onboarding.Step2.SignatureNotFound",
        "Nu există un pachet de semnături pentru acest dosar.");

    // Mesajul spune exact ce lipsește. Unul comun („TVA, contul bancar și Oblio”) pomenea și contul
    // bancar, pe care regula nu-l mai cere, iar omul nu știa ce să caute.
    public static readonly Error FiscalVatMissing = Error.Problem(
        "Onboarding.Step2.FiscalIncomplete",
        "Răspunde întâi la întrebarea despre TVA intracomunitar.");

    public static readonly Error FiscalOblioMissing = Error.Problem(
        "Onboarding.Step2.FiscalIncomplete",
        "Acceptă întâi acordurile pentru contul de facturare Oblio.");

    public static readonly Error RejectionReasonRequired = Error.Problem(
        "Onboarding.Step2.RejectionReasonRequired",
        "Respingerea are nevoie de un motiv, care se afișează clientului.");

    public static readonly Error VatProofMissing = Error.Problem(
        "Onboarding.Step2.VatProofMissing",
        "Încarcă întâi certificatul de TVA intracomunitar sau decizia ANAF.");
}

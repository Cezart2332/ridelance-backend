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

    public static readonly Error VatProofMissing = Error.Problem(
        "Onboarding.Step2.VatProofMissing",
        "Încarcă întâi certificatul de TVA intracomunitar sau decizia ANAF.");
}

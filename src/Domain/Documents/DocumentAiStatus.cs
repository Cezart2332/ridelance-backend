namespace Domain.Documents;

/// <summary>
/// Starea citirii automate (OCR) a unui document. Nu e un verdict: AI-ul doar citește, iar
/// documentul îl acceptă sau îl respinge un om din echipă (<see cref="DocumentStatus"/>).
/// </summary>
public enum DocumentAiStatus
{
    None = 0,
    Queued = 1,
    Processing = 2,

    /// <summary>Citit: câmpurile, datele și rotația sunt extrase.</summary>
    Read = 3,

    /// <summary>Nu s-a putut citi (fișier stricat, serviciul AI indisponibil). Nu blochează nimic.</summary>
    Error = 5
}

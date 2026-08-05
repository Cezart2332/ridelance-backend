namespace Domain.Documents;

/// <summary>
/// Tipul unui câmp extras prin OCR — determină validatorul determinist aplicat
/// (vezi <see cref="ExtractedFieldValidators"/>).
/// </summary>
public enum ExtractedFieldType
{
    Text = 0,
    Cui = 1,
    Iban = 2,
    Vin = 3,
    Plate = 4,
    Date = 5,
    Caen = 6,
    /// <summary>CNP — 13 cifre cu cifră de control. Mereu marcat și ca sensibil.</summary>
    Cnp = 7,
}

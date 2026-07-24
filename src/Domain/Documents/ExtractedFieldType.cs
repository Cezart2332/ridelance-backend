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
}

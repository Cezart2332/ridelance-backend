namespace Domain.Documents;

/// <summary>
/// Starea de verificare a unui câmp extras prin OCR. Nu blochează niciodată fluxul —
/// documentele cu încredere mică intră doar în coada de verificare a adminului.
/// </summary>
public enum ExtractedFieldReviewState
{
    /// <summary>Încredere mare + validator trecut — se folosește automat.</summary>
    Auto = 0,

    /// <summary>Se cere userului să confirme valoarea precompletată.</summary>
    NeedsUserConfirmation = 1,

    /// <summary>Încredere mică — intră în coada de verificare manuală a adminului.</summary>
    NeedsManualReview = 2,

    /// <summary>Confirmat de om.</summary>
    Confirmed = 3,
}

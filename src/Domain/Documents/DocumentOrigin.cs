namespace Domain.Documents;

/// <summary>
/// De unde vine documentul. Determină dacă îl mai arătăm șoferului: din tot ce ține dosarul, el
/// trebuie să vadă doar ce mai are de făcut (RL-07).
///
/// Backendul păstrează totul, indiferent de valoare — generatorul de dosar ignoră complet flagul.
/// </summary>
public enum DocumentOrigin
{
    /// <summary>Încărcat de utilizator. Singura valoare vizibilă în onboarding.</summary>
    UserUpload = 0,

    /// <summary>Precompletat de noi pe baza altor date, nu cerut de la șofer.</summary>
    Prefilled = 1,

    /// <summary>Moștenit dintr-un dosar anterior al aceluiași client.</summary>
    Inherited = 2,

    /// <summary>Generat de sistem (dosare ARR/copie conformă, specimen de semnătură).</summary>
    SystemGenerated = 3,
}

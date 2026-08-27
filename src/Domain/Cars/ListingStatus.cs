namespace Domain.Cars;

/// <summary>
/// Starea anunțului public — separată de starea mașinii (<see cref="CarStatus"/>).
///
/// Separarea e motivul pentru care există enumul ăsta. Până acum exista un singur comutator,
/// <c>Active</c>, iar „scoate anunțul de pe piață" și „mașina nu mai e în flotă" ajungeau amândouă
/// în el. Un anunț pus pe pauză nu spune nimic despre mașină: închirierile continuă, dosarul rămâne,
/// mentenanța se face mai departe.
/// </summary>
public enum ListingStatus
{
    /// <summary>Creat, nepublicat încă. Aici ajunge orice anunț nou al unei flote.</summary>
    Draft,

    /// <summary>Vizibil în marketplace.</summary>
    Published,

    /// <summary>Retras temporar de proprietar. Se întoarce la <see cref="Published"/> fără să piardă nimic.</summary>
    Paused,

    /// <summary>Retras definitiv. Ține locul ștergerii, ca istoricul închirierilor să rămână citibil.</summary>
    Archived,
}
